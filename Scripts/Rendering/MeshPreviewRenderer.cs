using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Rendering {
    //One live preview and one thumbnail render at a time, all in worlds independent of the city.
    public partial class MeshPreviewRenderer : Node {
        const int CacheLimit = 128;
        readonly Dictionary<string, (Texture2D texture, LinkedListNode<string> node)> cache = new(System.StringComparer.OrdinalIgnoreCase);
        readonly LinkedList<string> recent = new();
        readonly Queue<ThumbnailJob> requests = new();
        class ThumbnailJob {
            public string path;
            public TaskCompletionSource<Texture2D> reply = new();
            public SubViewport viewport;
        }

        public Control display;
        public System.Action<Texture2D, bool> previewChanged;
        SubViewport selectedViewport;
        Node3D pivot;
        ThumbnailJob running;
        string selectedPath;
        bool selectionPending;
        internal bool HasPreview => IsInstanceValid(selectedViewport);
        internal float Angle => IsInstanceValid(pivot) ? pivot.Rotation.Y : 0;
        internal int CachedThumbnails => cache.Count;

        bool Visible => IsInstanceValid(display) && display.IsVisibleInTree();

        public void SetMesh(string path) {
            ClearSelected();
            selectedPath = path;
            selectionPending = !string.IsNullOrEmpty(path);
        }

        public Task<Texture2D> GetThumbnail(string path) {
            if (string.IsNullOrEmpty(path) || !Visible || !CityRenderer.Available) return Task.FromResult<Texture2D>(null);
            if (cache.TryGetValue(path, out var entry)) {
                recent.Remove(entry.node);
                recent.AddFirst(entry.node);
                return Task.FromResult(entry.texture);
            }
            var job = new ThumbnailJob { path = path };
            requests.Enqueue(job);
            return job.reply.Task;
        }

        void Remember(string path, Texture2D texture) {
            if (cache.TryGetValue(path, out var old)) recent.Remove(old.node);
            cache[path] = (texture, recent.AddFirst(path));
            while (cache.Count > CacheLimit) {
                cache.Remove(recent.Last.Value);
                recent.RemoveLast();
            }
        }

        public void ClearCache() {
            CancelThumbnails();
            cache.Clear();
            recent.Clear();
        }

        static void Release(SubViewport viewport) {
            if (!IsInstanceValid(viewport)) return;
            viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            viewport.QueueFree();
        }

        void ClearSelected() {
            Release(selectedViewport);
            selectedViewport = null;
            pivot = null;
            previewChanged?.Invoke(null, false);
        }

        void CancelThumbnails() {
            while (requests.Count > 0) requests.Dequeue().reply.TrySetResult(null);
            if (running != null) {
                Release(running.viewport);
                running.viewport = null;
                running.reply.TrySetResult(null);
                running = null;
            }
        }

        public void Stop() {
            selectionPending = false;
            selectedPath = null;
            ClearSelected();
            CancelThumbnails();
        }

        public override void _ExitTree() {
            previewChanged = null;
            Stop();
            cache.Clear();
            recent.Clear();
        }

        SubViewport CreateViewport(string path, Vector2I size, out Node3D rotationRoot) {
            rotationRoot = null;
            Node3D imported = null, geometry = null;
            SubViewport viewport = null;
            try {
                imported = MeshImporter.LoadMesh(path, null, true, false);
                if (imported == null) return null;
                geometry = GameEngine.Mesh.CreatePreviewGeometry(imported);
                Aabb? bounds = null;
                foreach (MeshInstance3D child in geometry.GetChildren()) {
                    var box = child.Transform * child.GetAabb();
                    bounds = bounds.HasValue ? bounds.Value.Merge(box) : box;
                }
                if (!bounds.HasValue || !bounds.Value.Position.IsFinite() || !bounds.Value.Size.IsFinite()) return null;
                var radius = bounds.Value.Size.Length() * 0.5f;
                if (!float.IsFinite(radius) || radius < 0.000001f) return null;
                viewport = new SubViewport {
                    Name = "MeshPreviewViewport", OwnWorld3D = true, TransparentBg = true,
                    Size = size, GuiDisableInput = true, Msaa3D = Viewport.Msaa.Msaa2X,
                    RenderTargetUpdateMode = SubViewport.UpdateMode.Always
                };
                rotationRoot = new Node3D { Name = "Turntable", Scale = Vector3.One / radius };
                geometry.Position = -bounds.Value.GetCenter();
                rotationRoot.AddChild(geometry);
                geometry = null; //owned by the viewport from here
                viewport.AddChild(rotationRoot);
                var camera = new Camera3D {
                    Projection = Camera3D.ProjectionType.Orthogonal,
                    KeepAspect = Camera3D.KeepAspectEnum.Height,
                    Size = 2.3f * Mathf.Max(1, (float)size.Y / size.X), Near = 0.01f, Far = 10,
                    CullMask = 1, Current = true,
                    Environment = new Godot.Environment {
                        BackgroundMode = Godot.Environment.BGMode.Color,
                        BackgroundColor = new Color(0, 0, 0, 0),
                        AmbientLightSource = Godot.Environment.AmbientSource.Color,
                        AmbientLightColor = Colors.White, AmbientLightEnergy = 0.65f,
                        ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled
                    }
                };
                viewport.AddChild(camera);
                camera.Position = CameraFraming.Direction(25, 30) * 4;
                //Local transform also works before the viewport joins the tree.
                camera.Transform = camera.Transform.LookingAt(Vector3.Zero, Vector3.Up);
                viewport.AddChild(new DirectionalLight3D {
                    RotationDegrees = new Vector3(-45, -30, 0), LightEnergy = 0.8f, ShadowEnabled = false
                });
                AddChild(viewport);
                return viewport;
            } catch (System.Exception e) {
                GameEngine.LogWarning("Unable to preview mesh " + path + ": " + e.Message);
                if (IsInstanceValid(viewport)) {
                    if (viewport.IsInsideTree()) Release(viewport);
                    else viewport.Free();
                }
                return null;
            } finally {
                if (IsInstanceValid(imported)) imported.Free();
                if (IsInstanceValid(geometry)) geometry.Free();
            }
        }

        async void RenderThumbnail(ThumbnailJob job) {
            running = job;
            try {
                job.viewport = CreateViewport(job.path, new Vector2I(64, 64), out var turntable);
                Texture2D texture = null;
                if (job.viewport != null) {
                    //Single-sided facades may be invisible from one side. Keep the most visible of four views.
                    int mostPixels = -1;
                    for (int side = 0; side < 4; side++) {
                        turntable.Rotation = new Vector3(0, side * Mathf.Pi * 0.5f, 0);
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        if (running != job || !IsInstanceValid(this) || !IsInsideTree()) return;
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        if (running != job || !IsInstanceValid(this) || !IsInsideTree() || !Visible) return;
                        using var image = job.viewport.GetTexture().GetImage();
                        if (image == null || image.IsEmpty()) continue;
                        image.Convert(Godot.Image.Format.Rgba8);
                        var pixels = image.GetData();
                        int visiblePixels = 0;
                        for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] > 10) visiblePixels++;
                        if (visiblePixels > mostPixels) {
                            mostPixels = visiblePixels;
                            texture = ImageTexture.CreateFromImage(image);
                        }
                    }
                }
                if (running == job) {
                    Remember(job.path, texture);
                    job.reply.TrySetResult(texture);
                }
            } catch (System.Exception e) {
                GameEngine.LogWarning("Unable to render mesh thumbnail: " + e.Message);
            } finally {
                Release(job.viewport);
                job.viewport = null;
                job.reply.TrySetResult(null);
                if (running == job) running = null;
            }
        }

        public override void _Process(double delta) {
            if (!Visible) {
                if (selectionPending || HasPreview || running != null || requests.Count > 0) Stop();
                return;
            }
            if (selectionPending) {
                selectionPending = false;
                if (CityRenderer.Available) {
                    var size = display.Size;
                    selectedViewport = CreateViewport(selectedPath, new Vector2I(Mathf.Clamp((int)size.X, 64, 512), Mathf.Clamp((int)size.Y, 64, 512)), out pivot);
                }
                previewChanged?.Invoke(HasPreview ? selectedViewport.GetTexture() : null, !HasPreview);
                return; //a new selection always precedes queued thumbnails
            }
            if (IsInstanceValid(pivot)) pivot.RotateY((float)delta * Mathf.Tau / 10);
            if (running == null && requests.Count > 0) RenderThumbnail(requests.Dequeue());
        }
    }
}
