using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using CT = CityElements.Types;
using TP = CityElements.Types.Parsers.TypeParser;
using PC = Operations.PropertyContainers;
using GO = GameEngine.GameObject;

namespace Api {
    public partial class ApiSelfTest {
        //the label elements of a layout, with fields around them
        class LabelsPanel : EditorPanel {
            public override void Initialize(Control canvas) {
                Initialize(canvas, 1);
                var state = new ObjectState();
                getObject = () => state;
                CT.Parameter Field(string name) => new CT.Parameter { name = name, label = "RACE_NAME", type = "float" };
                CT.TabElement Elem(string name, float width, float height = 0) => new CT.TabElement { name = name, width = width, height = height };
                var parameters = new[] { Field("a"), Field("b"), Field("c"), Field("d"), Field("e"), Field("f") };
                var curW = 0.0f;
                foreach (var elem in new[] {
                    Elem("label:RACE_NAME", 1.5f), Elem("a", 0.75f), Elem("b", 0.75f),
                    Elem("label:RACE_AMATEUR", 0.75f), Elem("label:RACE_PROFESSIONAL", 0.75f, 0.2f), Elem("c", 0.75f), Elem("d", 0.75f),
                    Elem("label:RACE_PROPS", 1.5f, 0.2f), Elem("e", 1.5f),
                    Elem("label:RACE_OPPONENTS", 1.5f, 0.8f), Elem("f", 1.5f)
                }) {
                    AddParameters(GetPage(0), ref curW, 1.5f, parameters, elem, getObject, false);
                }
                GetPage(0).EnsureNewRow();
            }
        }

        //two array lists, to check that their buttons follow only their own list
        class ArrayListsPanel : EditorPanel {
            public ObjectState state = new();
            public CT.Parameter[] parameters;

            public ArrayListsPanel() => AddComplexElement(new IOList(this));

            public override void Initialize(Control canvas) {
                Initialize(canvas, 1);
                getObject = () => state;
                float width = 0;
                foreach (var parameter in parameters) {
                    AddParameters(GetPage(0), ref width, 1.5f, parameters, new CT.TabElement { name = parameter.name, width = 1.5f }, () => state, false);
                }
            }

            public EditorPanelElements.ScrollList List(string name) => PreviewField<Dictionary<string, IOList.ListTuple>>(GetComplexElement<IOList>(), "listControls")[name].list;
            public bool Enabled(string name, IOListButtonMode mode) => PreviewField<List<(string key, IOListButtonMode mode, EditorPanelElements.Button btn)>>(GetComplexElement<IOList>(), "extraButtons")
                .First(b => b.key == name && b.mode == mode).btn.GetInteractable();
        }

        void ArrayListMoveButtons() {
            Log("array list move buttons");
            CT.Parameter List(string name) => new CT.Parameter { name = name, type = "array", arrayProperties = new CT.ArrayProperties {
                elementLabel = "name", elementProperties = new[] { new CT.Parameter { name = "name", type = "string" } } } };
            ObjectState[] Elements(int count) => Enumerable.Range(0, count).Select(i => { var e = new ObjectState(); e.SetStr("name", "Element " + i); return e; }).ToArray();
            var panel = new ArrayListsPanel { parameters = new[] { List("three"), List("one") } };
            panel.state.SetArray("three", Elements(3));
            panel.state.SetArray("one", Elements(1));
            panel.Initialize(builder);
            panel.SetActive(true);
            try {
                const EditorPanel.IOListButtonMode up = EditorPanel.IOListButtonMode.ElementSelectedNotFirst, down = EditorPanel.IOListButtonMode.ElementSelectedNotLast;
                Check("without a selection the move buttons are disabled", !panel.Enabled("three", up) && !panel.Enabled("three", down));
                panel.List("three").SetValue(0);
                Check("the first element can only move down", !panel.Enabled("three", up) && panel.Enabled("three", down));
                panel.List("three").SetValue(1);
                Check("a middle element can move both ways", panel.Enabled("three", up) && panel.Enabled("three", down));
                panel.List("three").SetValue(2);
                Check("the last element can only move up", panel.Enabled("three", up) && !panel.Enabled("three", down));
                Check("selecting in a list leaves the move buttons of the other list", !panel.Enabled("one", up) && !panel.Enabled("one", down));
                panel.List("one").SetValue(0);
                Check("the only element of a list can't move", !panel.Enabled("one", up) && !panel.Enabled("one", down));
                Check("selecting in a list keeps the move buttons of the other list", panel.Enabled("three", up) && !panel.Enabled("three", down));
                panel.RefreshValues();
                Check("reloading the lists disables the move buttons again", !panel.Enabled("three", up) && !panel.Enabled("three", down));
            } finally { panel.Destroy(); }
        }

        async Task RaceSubcontainers() {
            Log("race difficulty subcontainers");
            builder.CloseAllPanels();
            var city = M.GetDummy<CityProperties>();
            var race = city.state.Array<ObjectState>("circuitRaces").First(r => PC.Contents(PC.EnsureId(r)).OfType<CustomElement>().Any(e => e.TypeName == "opponent"));
            var owner = PC.EnsureId(race);
            var amateurId = PC.EnsureId(race.State("policeCars"));
            var professionalId = PC.EnsureId(race.State("policeCars_professional"));
            var amateur = PC.Contents(amateurId).OfType<CustomElement>().First();
            var prop = PC.Contents(owner).First(n => n is MeshInstance);
            void Select(string id) {
                builder.CloseAllPanels();
                PC.Open(builder, PC.Find(id));
                M.ProcessUpdate();
            }
            PopupMenu Create() {
                builder.ReloadCreateMenu();
                return CallBuilder<MenuBar>("GetMenuBar").FindChildren("*", nameof(MenuButton), true, false).OfType<MenuButton>()
                    .First(b => b.Text == StringManager.Get("CREATE")).GetPopup();
            }
            bool Has(PopupMenu menu, string key) => Enumerable.Range(0, menu.ItemCount).Any(i => menu.GetItemText(i) == StringManager.Get(key));
            void Command(string key) {
                var menu = Create();
                var index = Enumerable.Range(0, menu.ItemCount).First(i => menu.GetItemText(i) == StringManager.Get(key));
                menu.EmitSignal(PopupMenu.SignalName.IdPressed, menu.GetItemId(index));
            }
            bool PoliceVisible(string id) {
                var setup = PC.Find(id).state;
                return city.FindChildren("*", "", true, false).OfType<ArrayObject>()
                    .Where(a => a.state == setup).Any(a => a.IsVisibleInTree() && a.ProcessMode != ProcessModeEnum.Disabled
                        && a.FindChildren("*", "", true, false).OfType<MeshInstance>().Any(m => m.IsVisibleInTree()));
            }
            ArrayObject.activeElements["raceDifficulty"] = null;
            Select(owner);
            Check("a selected race alone exposes checkpoints but no difficulty commands", Has(Create(), "CREATE_RACE_CHECKPOINT") && !Has(Create(), "CREATE_RACE_POLICE") && !Has(Create(), "CREATE_OPPONENT"));
            Check("without a difficulty the race props stay visible and opponents are hidden", prop.IsVisibleInTree() && !amateur.IsVisibleInTree());
            Select(amateurId);
            Check("opening a subcontainer selects its parent and itself", PC.Active("race")?.state == PC.Find(owner).state && PC.Active("raceDifficulty")?.state == PC.Find(amateurId).state);
            Check("existing opponents belong to their difficulty without changing saved ownership", PC.Scope(amateur.state) == amateurId && PC.Owner(amateur.state) == owner && amateur.IsVisibleInTree());
            Check("the amateur police are visible and the professional police hidden", PoliceVisible(amateurId) && !PoliceVisible(professionalId));
            var bar = CallBuilder<MenuBar>("GetMenuBar");
            var containers = bar.FindChildren("*", nameof(MenuButton), true, false).OfType<MenuButton>()
                .First(b => b.Text == StringManager.Get("CONTAINERS_SELECTED_PLUR").Replace("$NUMBER", "2")).GetPopup();
            containers.EmitSignal(Window.SignalName.AboutToPopup);
            Check("the two selected containers include difficulty edit and deselect entries", Has(containers, "EDIT_RACE_AMATEUR") && Has(containers, "DESELECT_RACE_AMATEUR"));
            Select(professionalId);
            Check("switching difficulty hides amateur routes and switches police picking", !amateur.IsVisibleInTree() && amateur.ProcessMode == ProcessModeEnum.Disabled && !PoliceVisible(amateurId) && PoliceVisible(professionalId));
            Command("CREATE_OPPONENT");
            Check("a professional opponent draft inherits its selected difficulty", builder.customElementPlacer.creationState.Int("difficulty") == 1 && builder.customElementPlacer.propertyContainerId == professionalId);
            var hit = new GameEngine.Raycast.RaycastHit { collider = builder.helper, point = new Vector3(-100,0,-110) };
            History.UndoManager.Record("place professional opponent", () => builder.customElementPlacer.PlaceAt(hit));
            M.ProcessUpdate(); await Frames(2);
            var professional = builder.customElementPlacer.GetElement();
            Check("new opponents go into the selected professional subcontainer", professional.state.Int("difficulty") == 1 && PC.Scope(professional.state) == professionalId && PC.Contents(professionalId).Contains(professional) && !PC.Contents(amateurId).Contains(professional));
            History.UndoManager.Record("apply amateur preset to professional", () => {
                var preset = Operations.CustomElementPresets.Extract(amateur);
                Operations.CustomElementPresets.Apply(professional, preset);
            });
            M.ProcessUpdate();
            Check("presets preserve difficulty membership", professional.state.Int("difficulty") == 1 && PC.Scope(professional.state) == professionalId);
            builder.PerformUndo(); await Frames(2);
            Check("undo keeps both parent and difficulty selected", PC.Active("race")?.state == PC.Find(owner).state && PC.Active("raceDifficulty")?.state == PC.Find(professionalId).state && professional.IsVisibleInTree());
            Select(professionalId);
            Command("CREATE_OPPONENT");
            var count = M.customElements.Count;
            ArrayObject.activeElements["raceDifficulty"] = PC.Find(amateurId).state;
            builder.NotifyVisibilityChange(); M.ProcessUpdate();
            History.UndoManager.Record("stale professional draft", () => builder.customElementPlacer.PlaceAt(hit));
            Check("switching difficulty cancels the captured opponent draft", M.customElements.Count == count && builder.customElementPlacer.placementMode == ElementPlacer.CustomElementPlacer.PlacementMode.None);
            Select(professionalId);
            Command("CREATE_RACE_POLICE");
            var before = PC.Find(professionalId).state.Array<ObjectState>("policeCars").Length;
            History.UndoManager.Record("add professional police", () => builder.arrayPointPlacer.PlaceAt(new Vector3(-107,0,-110)));
            M.ProcessUpdate();
            Check("police creation targets the selected setup without a target dropdown", PC.Find(professionalId).state.Array<ObjectState>("policeCars").Length == before + 1);
            builder.PerformUndo(); await Frames(2);
            Check("undo restores police and keeps its subcontainer selected", PC.Active("raceDifficulty")?.state == PC.Find(professionalId).state && PC.Find(professionalId).state.Array<ObjectState>("policeCars").Length == before);
            CallBuilder<object>("DeselectEntity", "raceDifficulty"); M.ProcessUpdate();
            Check("deselecting a difficulty leaves its race and props selected", PC.Active("race") != null && PC.Active("raceDifficulty") == null && prop.IsVisibleInTree() && !professional.IsVisibleInTree());
            Select(professionalId);
            Command("CREATE_RACE_POLICE");
            CallBuilder<object>("DeselectEntity", "race"); M.ProcessUpdate();
            Check("deselecting the parent clears the child and its placement", PC.Active("race") == null && PC.Active("raceDifficulty") == null && !professional.IsVisibleInTree() && !prop.IsVisibleInTree());
            var policeCount = PC.Find(professionalId).state.Array<ObjectState>("policeCars").Length;
            builder.arrayPointPlacer.PlaceAt(new Vector3(-109,0,-110)); M.ProcessUpdate();
            Check("a deselected parent cannot receive police clicks", PC.Find(professionalId).state.Array<ObjectState>("policeCars").Length == policeCount);
            Select(professionalId);
            var param = TP.GetCitySettings()["city"].typeData.parametersInfo.parameters.First(p => p.name == "circuitRaces");
            History.UndoManager.Record("clone race with difficulty sets", () => {
                History.UndoManager.Touch(city);
                Operations.States.CloneArrayElement(city.state, param, Array.IndexOf(city.state.Array<ObjectState>("circuitRaces"), PC.Find(owner).state));
                M.FlagAsChanged();
            });
            M.ProcessUpdate();
            var copy = city.state.Array<ObjectState>("circuitRaces")[^1];
            Check("cloning creates independent subcontainers and copies each opponent once", PC.EnsureId(copy.State("policeCars")) != amateurId && PC.EnsureId(copy.State("policeCars_professional")) != professionalId
                && PC.Contents(PC.EnsureId(copy)).Count == PC.Contents(owner).Count && PC.Contents(PC.EnsureId(copy.State("policeCars_professional"))).Count == PC.Contents(professionalId).Count);
            builder.PerformUndo(); await Frames(2);
            //A different core may allow mesh instances in its filtered subcontainers too.
            var amateurSettings = PC.Find(amateurId).settings;
            var professionalSettings = PC.Find(professionalId).settings;
            var amateurTypes = amateurSettings.types;
            var professionalTypes = professionalSettings.types;
            try {
                amateurSettings.types = amateurTypes.Append("individualProp").ToArray();
                professionalSettings.types = professionalTypes.Append("individualProp").ToArray();
                History.UndoManager.Record("generic filtered mesh scopes", () => {
                    var a = Operations.Meshes.NewSettings("individualProp"); PC.Assign(a, amateurId);
                    var p = Operations.Meshes.NewSettings("individualProp"); PC.Assign(p, professionalId);
                    var point = new Vector3(-160,0,-160);
                    var aMesh = Operations.Meshes.Create(M, "race_test.obj", point, a);
                    var pMesh = Operations.Meshes.Create(M, "race_test.obj", point, p);
                    Check("a subcontainer eraser preserves the other subcontainer's overlapping meshes", Operations.Meshes.EraseInRadius(M, point, 1, p) == 1 && !aMesh.IsQueuedForDeletion() && pMesh.IsQueuedForDeletion());
                    aMesh.Delete(); M.FlagAsChanged();
                });
                M.ProcessUpdate(); await Frames(2);
            } finally {
                amateurSettings.types = amateurTypes;
                professionalSettings.types = professionalTypes;
            }
            builder.CloseAllPanels();
            await Call("city.save"); await Call("city.reload");
            Select(professionalId);
            Check("save and reload preserve subcontainer identities and difficulty membership", PC.Active("race")?.state == PC.Find(owner).state && PC.Active("raceDifficulty")?.state == PC.Find(professionalId).state
                && PC.Contents(professionalId).OfType<CustomElement>().Any(e => e.state.Int("difficulty") == 1 && e.IsVisibleInTree()));
            await Frames(2);
            Screenshot("race_professional_subcontainer.png");
            //the hudmap holds the whole city state, with the races in it, but closing it must leave them selected
            var cityPanel = builder.cityPropertiesEditorPanel;
            void OpenHudmap() {
                builder.CloseAllPanels();
                builder.SelectObject(M.GetDummy<CityProperties>());
                cityPanel.ShowTab("CP_HUDMAP");
                M.ProcessUpdate();
            }
            OpenHudmap();
            Check("a race and the hudmap can be selected together", PC.Active("race")?.state == PC.Find(owner).state && PC.Active("hudmap") != null);
            CallBuilder<object>("DeselectEntity", "hudmap"); M.ProcessUpdate();
            Check("closing the hudmap closes its tab and leaves the race and its difficulty selected", PC.Active("hudmap") == null && !cityPanel.IsOpen()
                && PC.Active("race")?.state == PC.Find(owner).state && PC.Active("raceDifficulty")?.state == PC.Find(professionalId).state);
            OpenHudmap();
            builder.CloseAllPanels();
            Select(owner);
            var racePanel = CallBuilder<EditorPanel>("GetActivePanel");
            CallBuilder<object>("DeselectEntity", "hudmap"); M.ProcessUpdate();
            Check("closing the hudmap leaves the panel of the race open", PC.Active("hudmap") == null && PC.Active("race")?.state == PC.Find(owner).state
                && racePanel is EditorPanels.ArrayElemEditorPanel && CallBuilder<EditorPanel>("GetActivePanel") == racePanel);
            OpenHudmap();
            CallBuilder<object>("DeselectEntity", "race"); M.ProcessUpdate();
            Check("closing the race leaves the hudmap selected", PC.Active("race") == null && PC.Active("hudmap") != null);
            CallBuilder<object>("DeselectEntity", "hudmap"); M.ProcessUpdate();
            builder.CloseAllPanels();
        }

        //undoing a waypoint of an opponent keeps its race selected, so what the race owns stays shown; deleting the opponent
        //afterwards removes it from the manager
        async Task OpponentWaypointUndo() {
            Log("opponent waypoint undo");
            SelectionObj(Path.Combine(folder, "race_undo_test.obj"), new[] { new Vector3(-1,0,-1), new Vector3(1,0,-1), new Vector3(0,2,1) }, new[] { new[] {0,1,2} });
            var city = M.GetDummy<CityProperties>();
            var param = TP.GetCitySettings()["city"].typeData.parametersInfo.parameters.First(p => p.name == "circuitRaces");
            History.UndoManager.Record("add race", () => {
                History.UndoManager.Touch(city);
                Operations.States.AddArrayElement(city.state, param);
                M.FlagAsChanged();
            });
            M.ProcessUpdate();
            var race = city.state.Array<ObjectState>("circuitRaces")[^1];
            var owner = PC.EnsureId(race);
            ArrayObject.activeElements["race"] = race;
            ArrayObject.activeElements["raceDifficulty"] = race.State("policeCars");
            builder.NotifyVisibilityChange(); M.ProcessUpdate();
            History.UndoManager.Record("start opponent", () => CallBuilder<object>("StartCustomElementCreation", "opponent", "CREATE_CUSTOM_ELEMENT"));
            var hit = new GameEngine.Raycast.RaycastHit { collider = builder.helper, point = new Vector3(-100,0,-100) };
            History.UndoManager.Record("place start car", () => builder.customElementPlacer.PlaceAt(hit));
            M.FlagAsChanged(); await Frames(2);
            var opponent = builder.customElementPlacer.GetElement();
            foreach (var point in new[] { new Vector3(-90,0,-100), new Vector3(-80,0,-90), new Vector3(-70,0,-80) }) {
                hit.point = point;
                History.UndoManager.Record("waypoint", () => builder.customElementPlacer.PlaceAt(hit));
                await Frames(2);
            }
            Check("the opponent has its waypoints", opponent.GetAllPoints().Count == 3);
            builder.PerformUndo(); await Frames(3);
            bool LineShown() => opponent.generator.GetChildren().OfType<GameEngine.LineRenderer>().Any(l => l.enabled && l.IsVisibleInTree());
            Check("undo removes the last waypoint", opponent.GetAllPoints().Count == 2);
            Check("undo keeps the race selected", PC.Active("race")?.state == PC.Find(owner)?.state && PC.Active("race") != null);
            Check("undo keeps the opponent and its car shown", opponent.IsVisibleInTree() && opponent.ProcessMode != ProcessModeEnum.Disabled && opponent.FindParametricObject("carModel", new())?.IsVisibleInTree() == true);
            Check("undo keeps the opponent line shown", LineShown());
            Check("undo reopens the opponent in its placement mode", builder.customElementEditorPanel.IsOpen() && builder.customElementPlacer.GetElement() == opponent
                && builder.customElementPlacer.placementMode == ElementPlacer.CustomElementPlacer.PlacementMode.AppendPoint);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var pages = (System.Collections.IList)typeof(EditorPanel).GetField("pages", flags).GetValue(builder.customElementEditorPanel);
            var delete = pages.Cast<EditorPanelPage>().SelectMany(page => ((List<List<EditorPanelElement>>)typeof(EditorPanelPage).GetField("elements", flags).GetValue(page)).SelectMany(row => row))
                .OfType<EditorPanelElements.Button>().Select(b => (Godot.Button)typeof(EditorPanelElements.Button).GetField("button", flags).GetValue(b)).First(b => b.Text == StringManager.Get("DELETE"));
            //the physics step flushes the delete queue, so the update finds the element already freed, as after a real click
            //when a physics step comes between the input and _Process
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            delete.EmitSignal(Godot.Button.SignalName.Pressed);
            await Frames(3);
            Check("clicking delete deletes the opponent", !GodotObject.IsInstanceValid(opponent));
            Check("the deleted opponent leaves the manager", !M.customElements.Contains(opponent));
            Check("the update after the deletion completes", !M.worldChanged);
            var settings = new ObjectState();
            PC.Assign(settings, owner);
            var prop = Operations.Meshes.Create(M, "race_undo_test.obj", new Vector3(-100,0,-90), settings);
            M.ProcessUpdate();
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            prop.Delete();
            M.FlagAsChanged();
            await Frames(3);
            Check("a deleted owned mesh leaves the manager", !M.meshes.Contains(prop) && !M.worldChanged);
            builder.CloseAllPanels();
            ArrayObject.activeElements["race"] = null;
            builder.NotifyVisibilityChange(); M.ProcessUpdate();
        }

        async Task RaceCreation() {
            Log("race ownership and contextual creation");
            const string mesh = "race_test.obj";
            SelectionObj(Path.Combine(folder, mesh), new[] { new Vector3(-1,0,-1), new Vector3(1,0,-1), new Vector3(0,2,1) }, new[] { new[] {0,1,2} });
            var city = M.GetDummy<CityProperties>();
            var param = TP.GetCitySettings()["city"].typeData.parametersInfo.parameters.First(p => p.name == "circuitRaces");
            History.UndoManager.Record("add races", () => {
                History.UndoManager.Touch(city);
                Operations.States.AddArrayElement(city.state, param);
                Operations.States.AddArrayElement(city.state, param);
                city.state.Array<ObjectState>("circuitRaces")[0].SetStr("name", "Race A");
                city.state.Array<ObjectState>("circuitRaces")[1].SetStr("name", "Race B");
                M.FlagAsChanged();
            });
            M.ProcessUpdate();
            var race = city.state.Array<ObjectState>("circuitRaces")[0];
            var other = city.state.Array<ObjectState>("circuitRaces")[1];
            var owner = PC.EnsureId(race);
            var otherOwner = PC.EnsureId(other);
            void Select(ObjectState state) {
                ArrayObject.activeElements["race"] = state;
                ArrayObject.activeElements["raceDifficulty"] = state?.State("policeCars");
                builder.NotifyVisibilityChange();
                M.ProcessUpdate();
            }
            PopupMenu Menu() => CallBuilder<MenuBar>("GetMenuBar").FindChildren("*", nameof(MenuButton), true, false).OfType<MenuButton>()
                .First(b => b.Text == StringManager.Get("CREATE")).GetPopup();
            bool Has(string key) => Enumerable.Range(0, Menu().ItemCount).Any(i => Menu().GetItemText(i) == StringManager.Get(key));
            Select(null); builder.ReloadCreateMenu();
            Check("race creation commands are absent without a selected race", !Has("CREATE_OPPONENT") && !Has("CREATE_RACE_CHECKPOINT") && !Has("CREATE_RACE_POLICE"));
            Select(race); builder.ReloadCreateMenu();
            Check("race selection exposes opponent, checkpoint and police creation", Has("CREATE_OPPONENT") && Has("CREATE_RACE_CHECKPOINT") && Has("CREATE_RACE_POLICE"));
            Check("race identities differ and capture does not use indices", owner != otherOwner && PC.Capture("race", "individualProp") == owner);
            void Command(string key) {
                builder.ReloadCreateMenu();
                var index = Enumerable.Range(0, Menu().ItemCount).First(i => Menu().GetItemText(i) == StringManager.Get(key));
                Menu().EmitSignal(PopupMenu.SignalName.IdPressed, Menu().GetItemId(index));
            }
            Command("CREATE_RACE_CHECKPOINT");
            History.UndoManager.Record("place race checkpoint", () => builder.arrayPointPlacer.PlaceAt(new Vector3(-110,0,-110)));
            M.ProcessUpdate();
            Check("Create checkpoint places into the selected race", race.Array<ObjectState>("checkpoints")?.Length == 1 && other.Array<ObjectState>("checkpoints") == null);
            builder.CloseAllPanels();
            Command("CREATE_RACE_POLICE");
            History.UndoManager.Record("place amateur police", () => builder.arrayPointPlacer.PlaceAt(new Vector3(-111,0,-111)));
            builder.CloseAllPanels();
            ArrayObject.activeElements["raceDifficulty"] = race.State("policeCars_professional");
            builder.NotifyVisibilityChange(); M.ProcessUpdate();
            Command("CREATE_RACE_POLICE");
            History.UndoManager.Record("place professional police", () => builder.arrayPointPlacer.PlaceAt(new Vector3(-112,0,-112)));
            M.ProcessUpdate();
            Check("police placement selects both difficulty targets", race.State("policeCars")?.Array<ObjectState>("policeCars")?.Length == 1 && race.State("policeCars_professional")?.Array<ObjectState>("policeCars")?.Length == 1);
            builder.CloseAllPanels();
            Select(race);
            History.UndoManager.Record("start opponent", () => CallBuilder<object>("StartCustomElementCreation", "opponent", "CREATE_CUSTOM_ELEMENT"));
            Check("opponent draft opens before placement", builder.customElementEditorPanel.IsOpen() && builder.customElementPlacer.GetElement() == null);
            Check("opponent draft exposes the car model before placement", builder.customElementPlacer.creationState.State("carModel") != null);
            builder.customElementPlacer.creationState.State("carModel").SetStr("meshPath", mesh);
            var hit = new GameEngine.Raycast.RaycastHit { collider = builder.helper, point = new Vector3(-100,0,-100) };
            History.UndoManager.Record("place start car", () => builder.customElementPlacer.PlaceAt(hit));
            M.FlagAsChanged(); M.ProcessUpdate(); await Frames(2);
            var opponent = builder.customElementPlacer.GetElement();
            var opponentId = History.ElementRegistry.GetId(opponent);
            Check("first opponent click positions only the start car", opponent != null && opponent.GetAllPoints().Count == 0 && opponent.state.State("carModel").Vector3("localPosition") == hit.point);
            Check("opponent has a separate clickable parametric car", opponent.FindParametricObject("carModel", new()) != null);
            Check("placement preserves the model chosen in the draft", opponent.FindParametricObject("carModel", new()).meshPath == mesh);
            var car = opponent.FindParametricObject("carModel", new());
            History.UndoManager.Record("click opponent car", () => CallBuilder<bool>("SelectCityPropertiesObject", car));
            var transformPanel = (EditorPanels.TransformPropertiesEditorPanel)typeof(CityBuilderMenuBar).GetField("transformPropertiesEditorPanel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(builder);
            Check("clicking the start car opens its transform editor", transformPanel.IsOpen() && transformPanel.instance == car);
            var originalStart = opponent.state.State("carModel").Vector3("localPosition");
            History.UndoManager.Record("move opponent car", () => {
                History.UndoManager.Touch(opponent);
                GO.SetPosition(car, originalStart + new Vector3(2,0,3));
                car.SyncParametricObjectState(); M.FlagAsChanged();
            });
            M.ProcessUpdate();
            Check("editing the start car preserves its live transform target", GodotObject.IsInstanceValid(car) && opponent.FindParametricObject("carModel", new()) == car && opponent.state.State("carModel").Vector3("localPosition") != originalStart);
            History.UndoManager.Undo(); await Frames(2);
            Check("start car transform supports undo", opponent.state.State("carModel").Vector3("localPosition").DistanceTo(originalStart) < 0.01f);
            builder.CloseAllPanels(); builder.SelectObject(opponent);
            builder.customElementPlacer.placementMode = ElementPlacer.CustomElementPlacer.PlacementMode.AppendPoint;
            builder.customElementPlacer.arrayPath = "spline";
            hit.point = new Vector3(-90,0,-100);
            History.UndoManager.Record("first waypoint", () => builder.customElementPlacer.PlaceAt(hit));
            hit.point = new Vector3(-80,0,-90);
            History.UndoManager.Record("second waypoint", () => builder.customElementPlacer.PlaceAt(hit));
            M.FlagAsChanged(); M.ProcessUpdate();
            Check("subsequent clicks append low-poly waypoint controls", opponent.GetAllPoints().Count == 2 && opponent.state.Int("curveType") == 2 && PC.Owner(opponent.state) == owner);
            var start = opponent.state.State("carModel").Vector3("localPosition");
            GO.SetPosition(opponent.GetAllPoints()[0].GetObject(), new Vector3(-85,3,-100));
            M.FlagAsChanged(); M.ProcessUpdate();
            Check("waypoint edits preserve the independent starting car", opponent.state.State("carModel").Vector3("localPosition") == start);
            builder.CloseAllPanels();
            foreach (var type in new[] { "propLine", "propLine", "propPolygon" }) {
                Select(race);
                History.UndoManager.Record("create owned " + type, () => CallBuilder<object>("StartCustomElementCreation", type, "CREATE_CUSTOM_ELEMENT"));
                History.UndoManager.Record("place owned " + type, () => builder.customElementPlacer.PlaceAt(hit));
                Check(type + " binds to the captured race", PC.Owner(builder.customElementPlacer.GetElement().state) == owner);
                builder.CloseAllPanels();
            }
            var lineElement = PC.Contents(owner).OfType<CustomElement>().First(e => e.TypeName == "propLine");
            var stripElement = PC.Contents(owner).OfType<CustomElement>().Last(e => e.TypeName == "propLine");
            History.UndoManager.Record("share race placer controls", () => {
                var point = lineElement.GetAllPoints()[0];
                Operations.CustomElements.AppendPoint(M, stripElement, "spline", point.GetObject(), point.GetPoint());
                var preset = Operations.CustomElementPresets.Extract(lineElement);
                Check("placer presets exclude race membership", PC.Owner(preset) == "");
                Operations.CustomElementPresets.Apply(lineElement, preset);
                Check("applying a placer preset preserves race membership", PC.Owner(lineElement.state) == owner);
            });
            Select(race);
            var settings = Operations.Meshes.NewSettings("individualProp"); PC.Assign(settings, owner);
            var globalSettings = Operations.Meshes.NewSettings("individualProp");
            History.UndoManager.Record("scatter race props", () => {
                var scattered = Operations.Meshes.Scatter(M, mesh, new Vector3(-140,0,-100), 2, 3, settings, false);
                Check("mass placement copies race ownership to every prop", scattered.Count == 3 && scattered.All(m => PC.Owner(m.settings) == owner));
                foreach (var instance in scattered) instance.Delete();
            });
            History.UndoManager.Record("props in two scopes", () => {
                Operations.Meshes.Create(M, mesh, new Vector3(-120,0,-100), settings);
                Operations.Meshes.Create(M, mesh, new Vector3(-120,0,-100), globalSettings);
            });
            M.FlagAsChanged(); M.ProcessUpdate();
            Check("owned props appear in their race contents", PC.Contents(owner).Count == 5);
            car = opponent.FindParametricObject("carModel", new());
            History.UndoManager.Record("edit owned car before hiding", () => CallBuilder<bool>("SelectCityPropertiesObject", car));
            Select(other);
            Check("another race hides owned geometry and picking", !opponent.Visible && opponent.ProcessMode == ProcessModeEnum.Disabled && PC.Contents(owner).OfType<MeshInstance>().All(m => !m.Visible));
            Check("hiding an owned start car also clears its transform gizmo", builder.gizmo.mainTargetRoot == null && !transformPanel.IsOpen());
            Select(race);
            Check("selecting its race restores geometry", opponent.Visible && PC.Contents(owner).OfType<MeshInstance>().All(m => m.Visible));
            History.UndoManager.Record("erase owned props", () => Operations.Meshes.EraseInRadius(M, new Vector3(-120,0,-100), 2, settings));
            M.FlagAsChanged(); M.ProcessUpdate();
            Check("race eraser preserves global props at the same position", M.meshes.Count(m => m.meshPath == mesh) == 1 && PC.Owner(M.meshes.First(m => m.meshPath == mesh).settings) == "");
            History.UndoManager.Undo(); await Frames(2);
            Select(race);
            History.UndoManager.Record("reorder races", () => { History.UndoManager.Touch(city); Operations.States.ShiftArrayElement(city.state, param, 0, true); M.FlagAsChanged(); });
            M.ProcessUpdate();
            Check("reordering keeps ownership and selection", PC.Owner(opponent.state) == owner && PC.Active("race")?.state == race);
            History.UndoManager.Record("clone race", () => { History.UndoManager.Touch(city); Operations.States.CloneArrayElement(city.state, param, 1); M.FlagAsChanged(); });
            M.ProcessUpdate();
            var clonedRace = city.state.Array<ObjectState>("circuitRaces")[2];
            var clonedOwner = PC.EnsureId(clonedRace);
            var clonedOpponent = PC.Contents(clonedOwner).OfType<CustomElement>().First(e => e.TypeName == "opponent");
            Check("cloning duplicates contents with a new owner", clonedOwner != owner && PC.Contents(clonedOwner).Count == PC.Contents(owner).Count);
            Check("cloned race controls are independent", clonedOpponent.GetAllPoints().Count == 2 && !clonedOpponent.GetAllPoints().Intersect(opponent.GetAllPoints()).Any());
            var copiedLine = PC.Contents(clonedOwner).OfType<CustomElement>().First(e => e.TypeName == "propLine");
            var copiedStrip = PC.Contents(clonedOwner).OfType<CustomElement>().Last(e => e.TypeName == "propLine");
            Check("race cloning preserves sharing inside the copy", copiedStrip.GetAllPoints().Contains(copiedLine.GetAllPoints()[0]) && copiedLine.GetAllPoints()[0] != lineElement.GetAllPoints()[0]);
            History.UndoManager.Record("delete race", () => { History.UndoManager.Touch(city); Operations.States.DeleteArrayElement(city.state, param, 2); M.FlagAsChanged(); });
            M.ProcessUpdate();
            Check("deleting a race removes its contents", PC.Contents(clonedOwner).Count == 0);
            History.UndoManager.Undo(); await Frames(3);
            Check("undo restores deleted race and owned contents together", PC.Find(clonedOwner) != null && PC.Contents(clonedOwner).Count == 5);
            History.UndoManager.Redo(); await Frames(3);
            Check("redo removes race and contents together", PC.Find(clonedOwner) == null && PC.Contents(clonedOwner).Count == 0);
            Select(PC.Find(owner).state);
            History.UndoManager.Record("start captured draft", () => CallBuilder<object>("StartCustomElementCreation", "opponent", "CREATE_CUSTOM_ELEMENT"));
            var count = M.customElements.Count;
            Select(PC.Find(otherOwner).state);
            History.UndoManager.Record("cancel stale draft", () => builder.customElementPlacer.PlaceAt(hit));
            Check("changing races cancels a captured placement", M.customElements.Count == count && builder.customElementPlacer.placementMode == ElementPlacer.CustomElementPlacer.PlacementMode.None);
            builder.CloseAllPanels();
            await Call("city.save"); await Call("city.reload");
            Check("save/reload retains container identities and opponent controls", PC.Find(owner) != null && PC.Contents(owner).OfType<CustomElement>().Any(e => e.TypeName == "opponent" && e.GetAllPoints().Count == 2));
            Select(PC.Find(owner).state);
            var json = Path.Combine(folder, "..", "races_export.json");
            await Call("city.exportJson", new { path = json });
            builder.ReloadCreateMenu();
            if (DisplayServer.GetName() != "headless") {
                await Call("view.camera.set", new { position = V(-110,35,-50), target = V(-95,0,-95) });
                Select(PC.Find(owner).state);
                builder.SelectObject(PC.Contents(owner).OfType<CustomElement>().First(e => e.TypeName == "opponent"));
                await Frames(3); Screenshot("race_opponent_editor.png");
                builder.CloseAllPanels(); builder.ReloadCreateMenu();
                Menu().Popup(new Rect2I(240, 60, 0, 0)); await Frames(4); Screenshot("race_create_menu.png");
                var propsIndex = Enumerable.Range(0, Menu().ItemCount).First(i => Menu().GetItemText(i) == StringManager.Get("PROPS_MENU"));
                var props = Menu().GetItemSubmenuNode(propsIndex);
                props.Popup(new Rect2I(Menu().Position.X + Menu().Size.X, Menu().Position.Y + 60, 0, 0));
                await Frames(3); Screenshot("race_props_menu.png"); props.Hide(); Menu().Hide();
            }
            await ContainerMenu();
        }

        //the containers menu (edit and deselect entries) and a container without owned types: the roam police cars
        async Task ContainerMenu() {
            Log("containers menu and roam police cars");
            builder.CloseAllPanels();
            var city = M.GetDummy<CityProperties>();
            ArrayObject.activeElements["raceDifficulty"] = null;
            var race = city.state.Array<ObjectState>("circuitRaces")[0];
            var param = TP.GetCitySettings()["city"].typeData.parametersInfo.parameters.First(p => p.name == "roamPoliceCars");
            ObjectState roam = null;
            History.UndoManager.Record("ensure roam police", () => {
                History.UndoManager.Touch(city);
                roam = Operations.States.EnsureCustomCityProperties(city.state, param);
            });
            void Select(ObjectState state) {
                ArrayObject.activeElements["race"] = state;
                builder.NotifyVisibilityChange();
            }
            var bar = CallBuilder<MenuBar>("GetMenuBar");
            PopupMenu Popup(string text) => bar.FindChildren("*", nameof(MenuButton), true, false).OfType<MenuButton>().First(b => b.Text == text).GetPopup();
            PopupMenu Containers() {
                var menu = Popup(StringManager.Get("CONTAINERS_SELECTED_SING").Replace("$NUMBER", "1"));
                menu.EmitSignal(Window.SignalName.AboutToPopup);
                return menu;
            }
            PopupMenu Create() {
                builder.ReloadCreateMenu();
                return Popup(StringManager.Get("CREATE"));
            }
            bool Has(PopupMenu menu, string text) => Enumerable.Range(0, menu.ItemCount).Any(i => menu.GetItemText(i) == text);
            void Press(PopupMenu menu, string text) {
                var index = Enumerable.Range(0, menu.ItemCount).First(i => menu.GetItemText(i) == text);
                menu.EmitSignal(PopupMenu.SignalName.IdPressed, menu.GetItemId(index));
            }
            var raceName = StringManager.Get("CUSTOM_ELEMENT_CATEGORY_RACE");
            var editRace = StringManager.Get("EDIT_CONTAINER").Replace("$CONTAINER", raceName);
            var deselectRace = StringManager.Get("DESELECT_CONTAINER").Replace("$CONTAINER", raceName);
            var editPolice = StringManager.Get("EDIT_POLICE_CARS");
            var hidePolice = StringManager.Get("HIDE_POLICE_CARS");
            EditorPanels.ArrayElemEditorPanel OpenPanel() => builder.cityPropertiesEditorPanel.childPanels.OfType<EditorPanels.ArrayElemEditorPanel>().FirstOrDefault(p => p.ActiveSelf());
            var curState = typeof(EditorPanels.ArrayElemEditorPanel).GetField("curState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            Select(race); M.ProcessUpdate();
            var entries = Containers();
            Check("a race has generic edit and deselect entries", Has(entries, editRace) && Has(entries, deselectRace));
            Press(entries, editRace); await Frames(1);
            Check("the edit entry opens the panel of the selected race", OpenPanel() is { } opened && curState.GetValue(opened) == race && ArrayObject.activeElements["race"] == race);
            builder.CloseAllPanels();
            Check("closing the panel keeps the race selected", ArrayObject.activeElements["race"] == race);

            //selected for the first time: it has no identity until the next update, the menu must catch up when it opens
            Select(roam);
            M.ProcessUpdate();
            entries = Containers();
            Check("roam police cars use their own menu labels", Has(entries, editPolice) && Has(entries, hidePolice) && !Has(entries, editRace) && !Has(entries, deselectRace));
            var create = Create();
            Check("roam police cars expose the police car entry only", Has(create, StringManager.Get("CREATE_RACE_POLICE")) && !Has(create, StringManager.Get("CREATE_RACE_CHECKPOINT")) && !Has(create, StringManager.Get("CREATE_OPPONENT")));
            Check("roam police cars own nothing", PC.Capture("race", "individualProp") == "" && PC.Capture("race", "opponent") == "");
            var before = roam.Array<ObjectState>("policeCars")?.Length ?? 0;
            Press(create, StringManager.Get("CREATE_RACE_POLICE"));
            Check("the police car entry opens the placement panel", builder.containerPlacementPanel.IsOpen());
            History.UndoManager.Record("place roam police", () => builder.arrayPointPlacer.PlaceAt(new Vector3(-113,0,-113)));
            M.ProcessUpdate();
            Check("the police car goes in the roam police cars", (roam.Array<ObjectState>("policeCars")?.Length ?? 0) == before + 1);
            History.UndoManager.Undo(); await Frames(2);
            roam = PC.Active("race")?.state;
            Check("undo removes the roam police car and keeps the selection", roam != null && (roam.Array<ObjectState>("policeCars")?.Length ?? 0) == before);
            builder.CloseAllPanels();

            Press(Containers(), editPolice); await Frames(1);
            var panel = OpenPanel();
            Check("the edit entry opens the roam police cars panel", panel != null && curState.GetValue(panel) == roam && ArrayObject.activeElements["race"] == roam);
            var contents = typeof(EditorPanels.ArrayElemEditorPanel).GetField("contentsPanel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Check("a container without owned types has no contents lists", panel != null && contents.GetValue(panel) == null);
            builder.CloseAllPanels();
            Press(Containers(), hidePolice);
            M.ProcessUpdate();
            Check("the hide entry deselects the roam police cars", ArrayObject.activeElements["race"] == null);

            Select(PC.Find(PC.EnsureId(race)).state); M.ProcessUpdate();
            Press(Containers(), editRace); await Frames(1);
            var racePanel = OpenPanel();
            Check("a race panel has its contents lists", racePanel != null && contents.GetValue(racePanel) != null);
            builder.CloseAllPanels();
            Select(null); M.ProcessUpdate();
            await RaceContents();
        }

        //the lists of the contents of a race, the way back from the elements, and deleting a checkpoint
        async Task RaceContents() {
            Log("race contents lists and navigation");
            builder.CloseAllPanels();
            await Call("city.save"); await Call("city.reload"); //the panels are still to be built, as in a new session
            var city = M.GetDummy<CityProperties>();
            var races = city.state.Array<ObjectState>("circuitRaces");
            var race = races.First(r => PC.Contents(PC.EnsureId(r)).Count > 0);
            var owner = PC.EnsureId(race);
            ArrayObject.activeElements["race"] = race;
            builder.NotifyVisibilityChange(); M.ProcessUpdate();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var transformPanel = (EditorPanels.TransformPropertiesEditorPanel)typeof(CityBuilderMenuBar).GetField("transformPropertiesEditorPanel", flags).GetValue(builder);
            IEnumerable<EditorPanelElement> Elements(EditorPanel panel) {
                var pages = (System.Collections.IList)typeof(EditorPanel).GetField("pages", flags).GetValue(panel);
                return pages.Cast<EditorPanelPage>().SelectMany(page => ((List<List<EditorPanelElement>>)typeof(EditorPanelPage).GetField("elements", flags).GetValue(page)).SelectMany(row => row));
            }
            Godot.Button ButtonOf(EditorPanel panel, string text) => Elements(panel).OfType<EditorPanelElements.Button>()
                .Select(b => (Godot.Button)typeof(EditorPanelElements.Button).GetField("button", flags).GetValue(b)).FirstOrDefault(b => b.Text == text);
            void Press(EditorPanel panel, string text) => ButtonOf(panel, text).EmitSignal(Godot.Button.SignalName.Pressed);
            EditorPanel Shown() => CallBuilder<EditorPanel>("GetActivePanel");
            List<Node3D> Listed(EditorPanels.ContainerContentsPanel panel) => (List<Node3D>)typeof(EditorPanels.ContainerContentsPanel).GetField("owned", flags).GetValue(panel);
            void Pick(EditorPanels.ContainerContentsPanel panel, Node3D node) {
                ((EditorPanelElements.ScrollList)typeof(EditorPanels.ContainerContentsPanel).GetField("list", flags).GetValue(panel)).SetValue(Listed(panel).IndexOf(node));
            }
            List<Godot.Button> ButtonsOf(EditorPanel panel, string text) => Elements(panel).OfType<EditorPanelElements.Button>()
                .Select(b => (Godot.Button)typeof(EditorPanelElements.Button).GetField("button", flags).GetValue(b)).Where(b => b.Text == text).ToList();
            string amateur = StringManager.Get("RACE_AMATEUR"), professional = StringManager.Get("RACE_PROFESSIONAL"), props = StringManager.Get("RACE_PROPS");
            void OpenOpponents(EditorPanel panel, string difficulty) {
                Press(panel, difficulty);
                Press(Shown(), StringManager.Get("RACE_OPPONENTS"));
            }
            List<List<EditorPanelElement>> Rows(EditorPanel panel) {
                var pages = (System.Collections.IList)typeof(EditorPanel).GetField("pages", flags).GetValue(panel);
                return (List<List<EditorPanelElement>>)typeof(EditorPanelPage).GetField("elements", flags).GetValue(pages[0]);
            }
            var goUp = StringManager.Get("ARRAY_ELEM_GO_UP");
            var back = StringManager.Get("BACK");

            var racePanel = PC.Open(builder, PC.Find(owner));
            Check("a race panel has both difficulty subcontainers and its props", ButtonsOf(racePanel, amateur).Count == 1 && ButtonsOf(racePanel, professional).Count == 1 && ButtonOf(racePanel, props) != null);
            Check("the amateur and professional values are side by side", Rows(racePanel).Count(r => r.OfType<EditorPanelElements.Dropdown>().Count() == 2) == 2 && Rows(racePanel).Count(r => r.OfType<EditorPanelElements.InputField>().Count() == 2) == 3);
            Check("the values have one label each", Rows(racePanel).Where(r => r.OfType<EditorPanelElements.Dropdown>().Count() == 2).All(r => r.OfType<EditorPanelElements.Label>().Count() == 0));
            Check("the checkpoints are not listed in the race panel", !Elements(racePanel).OfType<EditorPanelElements.ScrollList>().Any() && ButtonOf(racePanel, StringManager.Get("CHECKPOINTS")) != null);
            await Frames(2);
            Screenshot("race_contents_panel.png");
            Check("amateur is on the left of professional", ButtonOf(racePanel, amateur).GlobalPosition.X < ButtonOf(racePanel, professional).GlobalPosition.X);
            History.UndoManager.Record("different time of day", () => { race.SetInt("timeOfDay", 1); race.SetInt("timeOfDay_professional", 2); });
            builder.CloseAllPanels();
            racePanel = PC.Open(builder, PC.Find(owner));
            await Frames(2);
            var timeOfDay = Rows(racePanel).First(r => r.OfType<EditorPanelElements.Dropdown>().Count() == 2).OfType<EditorPanelElements.Dropdown>()
                .Select(d => (OptionButton)typeof(EditorPanelElements.Dropdown).GetField("dropdown", flags).GetValue(d)).OrderBy(o => o.GlobalPosition.X).ToArray();
            Check("the left field is the amateur value and the right one the professional", timeOfDay[0].Selected == 1 && timeOfDay[1].Selected == 2);
            Godot.Label TitleOf(EditorPanelElement element) => (Godot.Label)typeof(EditorPanelElement).GetField("titleLabel", flags).GetValue(element);
            var titled = Rows(racePanel).First(r => r.OfType<EditorPanelElements.Dropdown>().Count() == 2).OfType<EditorPanelElements.Dropdown>().Select(TitleOf).OrderBy(l => l.GlobalPosition.X).ToArray();
            Check("each field has its difficulty right above it", titled[0].Text == amateur && titled[1].Text == professional && titled[1].GlobalPosition.Y + titled[1].Size.Y <= timeOfDay[1].GlobalPosition.Y + 1);
            var shared = Elements(racePanel).OfType<EditorPanelElements.Label>().Select(TitleOf).First(l => l.Text == StringManager.Get("TIME_OF_DAY"));
            Check("the name of the parameter covers the row, above the difficulties", shared.GlobalPosition.Y + shared.Size.Y <= titled[0].GlobalPosition.Y + 1 && shared.Size.X > titled[0].Size.X * 1.5f);
            Check("the difficulties are not repeated as a heading", Elements(racePanel).OfType<EditorPanelElements.Label>().All(l => TitleOf(l).Text != amateur && TitleOf(l).Text != professional));
            History.UndoManager.Record("same time of day", () => { race.SetInt("timeOfDay", 0); race.SetInt("timeOfDay_professional", 0); });
            builder.CloseAllPanels();
            racePanel = PC.Open(builder, PC.Find(owner));
            var police = racePanel.OpenArrayElement("policeCars", null);
            await Frames(2);
            Check("the police cars panel opens", police != null && Shown() == police);
            Check("the difficulty panel offers police and opponents", ButtonOf(police, StringManager.Get("POLICE_CARS")) != null && ButtonOf(police, StringManager.Get("RACE_OPPONENTS")) != null);
            Press(police, StringManager.Get("POLICE_CARS"));
            Check("the police cars have no Add button", ButtonOf(Shown(), StringManager.Get("ARRAY_ADD")) == null && ButtonOf(Shown(), goUp) != null);
            Check("the back button of the panels is called Back", goUp == StringManager.Get("BACK"));
            Screenshot("race_police_panel.png");
            builder.CloseAllPanels();
            racePanel = PC.Open(builder, PC.Find(owner));
            Press(racePanel, StringManager.Get("CHECKPOINTS"));
            await Frames(2);
            var checkpointsList = Shown() as EditorPanels.ArrayListPanel;
            Check("the checkpoints open in a sub-panel", checkpointsList != null && Elements(checkpointsList).OfType<EditorPanelElements.ScrollList>().Count() == 1);
            Check("the checkpoints have no Add button but a hint", ButtonOf(checkpointsList, StringManager.Get("ARRAY_ADD")) == null && ButtonOf(checkpointsList, StringManager.Get("ARRAY_EDIT")) != null && Elements(checkpointsList).OfType<EditorPanelElements.Label>().Any());
            Screenshot("race_checkpoints_panel.png");
            var cityForUndo = M.GetDummy<CityProperties>();
            var checkpointsParam = TP.GetCustomCityPropertiesTypes()["circuitRace"].parameters.First(p => p.name == "checkpoints");
            var undoRace = race;
            History.UndoManager.Record("add a checkpoint in the sub-panel", () => {
                History.UndoManager.Touch(cityForUndo);
                Operations.States.AddArrayObjectAt(undoRace, checkpointsParam, new Vector3(-80,0,-70));
                M.FlagAsChanged();
            });
            M.ProcessUpdate();
            //what is selected in a list is outlined, while the list is shown
            bool Outlined(Node node) {
                var renderers = RuntimeGizmos.SelectionOutline.GetRenderers(node);
                return renderers.Count > 0 && renderers.All(r => (r.Layers & (1u << 16)) != 0);
            }
            var checkpointStates = undoRace.Array<ObjectState>("checkpoints");
            bool CheckpointOutlined(int i) => Outlined(Checkpoint(checkpointStates[i]));
            checkpointsList.RefreshValues();
            var checkpointItems = Elements(checkpointsList).OfType<EditorPanelElements.ScrollList>().First();
            await Frames(2);
            Check("no checkpoint is outlined before one is selected in the list", !CheckpointOutlined(0) && !CheckpointOutlined(1));
            checkpointItems.SetValue(1);
            await Frames(2);
            Check("the checkpoint selected in the list is outlined", CheckpointOutlined(1) && !CheckpointOutlined(0));
            Screenshot("race_checkpoint_outline.png");
            checkpointItems.SetValue(0);
            await Frames(2);
            Check("selecting another checkpoint moves the outline", CheckpointOutlined(0) && !CheckpointOutlined(1));
            checkpointsList.SetActive(false);
            await Frames(2);
            Check("hiding the list removes the outline", !CheckpointOutlined(0));
            checkpointsList.SetActive(true);
            await Frames(2);
            Check("showing the list again selects nothing", checkpointItems.Selected == -1 && !CheckpointOutlined(0));
            checkpointItems.SetValue(0);
            await Frames(2);
            checkpointsList.RefreshValues();
            await Frames(2);
            Check("deselecting in the list removes the outline", checkpointItems.Selected == -1 && !CheckpointOutlined(0));
            History.UndoManager.Undo(); await Frames(3);
            race = PC.Find(owner).state;
            Check("undo reopens the checkpoints sub-panel", Shown() is EditorPanels.ArrayListPanel);
            Press(Shown(), goUp);
            await Frames(1);
            Check("back from the checkpoints goes to the race", Shown() is EditorPanels.ArrayElemEditorPanel);
            builder.CloseAllPanels();
            racePanel = PC.Open(builder, PC.Find(owner));
            var opponent = PC.Contents(owner).OfType<CustomElement>().First(e => e.TypeName == "opponent");
            opponent.state.SetInt("difficulty", 0);

            Press(racePanel, props);
            var list = Shown() as EditorPanels.ContainerContentsPanel;
            Check("the props list shows the props only", list != null && Listed(list).Count > 0 && Listed(list).All(n => n is MeshInstance || ((CustomElement)n).TypeName != "opponent"));
            Check("the race stays selected in its lists", ArrayObject.activeElements["race"] == race);
            await Frames(2);
            Screenshot("race_contents_props.png");
            var outlinedProp = Listed(list).First(n => RuntimeGizmos.SelectionOutline.GetRenderers(n).Count > 0);
            Pick(list, outlinedProp);
            await Frames(2);
            Check("the prop selected in the props list is outlined", Outlined(outlinedProp));
            var line = Listed(list).OfType<CustomElement>().First(e => e.TypeName == "propLine");
            line.state.State("propRule")?.State("main")?.SetStr("mesh", "race_test.obj");
            Pick(list, line);
            Press(list, StringManager.Get("EDIT"));
            await Frames(2);
            Check("a prop line opens from its list as the first panel of the session", Shown() == builder.customElementEditorPanel && builder.customElementPlacer.GetElement() == line);
            Screenshot("race_contents_propline.png");
            Press(builder.customElementEditorPanel, back);
            await Frames(1);
            Check("back from a prop line goes to its list", Shown() is EditorPanels.ContainerContentsPanel again && Listed(again).Contains(line));
            list = (EditorPanels.ContainerContentsPanel)Shown();
            var strip = Listed(list).OfType<CustomElement>().Last(e => e.TypeName == "propLine");
            Pick(list, strip);
            Press(list, StringManager.Get("EDIT"));
            await Frames(1);
            Check("a second prop line opens from its list", Shown() == builder.customElementEditorPanel && builder.customElementPlacer.GetElement() == strip);
            builder.CloseAllPanels();

            //the parametric object edited last must not change how a plain prop is shown when the panel is rebuilt
            MeshInstance Checkpoint(ObjectState state) => city.FindChildren("*", "", true, false).OfType<MeshInstance>().First(m => {
                for (Node node = m; node != null; node = node.GetParent()) if (node is ArrayObject a) return a.state == state;
                return false;
            });
            History.UndoManager.Record("click checkpoint", () => CallBuilder<bool>("SelectCityPropertiesObject", Checkpoint(race.Array<ObjectState>("checkpoints")[0])));
            Check("clicking a checkpoint opens its transform panel", Shown() == transformPanel);
            Press(transformPanel, back);
            await Frames(1);
            Check("back from a checkpoint clicked in the city goes to the checkpoints", Shown() is EditorPanels.ArrayListPanel);
            builder.CloseAllPanels();
            History.UndoManager.Record("click checkpoint", () => CallBuilder<bool>("SelectCityPropertiesObject", Checkpoint(race.Array<ObjectState>("checkpoints")[0])));
            History.UndoManager.Record("close", () => transformPanel.Terminate());
            builder.CloseAllPanels();
            transformPanel.DeferInitialize(builder);
            racePanel = PC.Open(builder, PC.Find(owner));
            Press(racePanel, props);
            list = (EditorPanels.ContainerContentsPanel)Shown();
            var prop = Listed(list).OfType<MeshInstance>().First();
            Pick(list, prop);
            Press(list, StringManager.Get("EDIT"));
            await Frames(1);
            Check("an individual prop opens from its list when its panel is rebuilt", Shown() == transformPanel && transformPanel.instance == prop);
            Screenshot("race_contents_prop.png");
            Press(transformPanel, back);
            await Frames(1);
            Check("back from an individual prop goes to its list", Shown() is EditorPanels.ContainerContentsPanel propList && Listed(propList).Contains(prop));
            list = (EditorPanels.ContainerContentsPanel)Shown();
            var before = PC.Contents(owner).Count;
            Pick(list, prop);
            Press(list, StringManager.Get("DELETE"));
            M.FlagAsChanged(); M.ProcessUpdate();
            Check("deleting from a list updates it and stays there", Shown() == list && !Listed(list).Contains(prop) && PC.Contents(owner).Count == before - 1);
            History.UndoManager.Undo(); await Frames(3);
            Check("undo restores the deleted prop and reopens the list", PC.Contents(owner).Count == before && Shown() is EditorPanels.ContainerContentsPanel reopened && Listed(reopened).Count == PC.Contents(owner, PC.ContentGroups(PC.Find(owner).settings)[0]).Count);
            list = (EditorPanels.ContainerContentsPanel)Shown();
            Pick(list, Listed(list).OfType<CustomElement>().Last(e => e.TypeName == "propLine"));
            Press(list, StringManager.Get("DELETE"));
            M.FlagAsChanged(); M.ProcessUpdate();
            Check("a custom element can be deleted from a list", PC.Contents(owner).Count == before - 1);
            History.UndoManager.Undo(); await Frames(3);
            Check("undo restores a custom element deleted from a list", PC.Contents(owner).Count == before && PC.Contents(owner).OfType<CustomElement>().Count(e => e.TypeName == "propLine") == 2);
            list = (EditorPanels.ContainerContentsPanel)Shown();
            prop = Listed(list).OfType<MeshInstance>().First();
            Pick(list, prop);
            Press(list, StringManager.Get("EDIT"));
            await Frames(1);
            Press(transformPanel, StringManager.Get("DELETE"));
            M.FlagAsChanged(); M.ProcessUpdate(); await Frames(1);
            Check("deleting a prop opened from a list goes back to the list", Shown() is EditorPanels.ContainerContentsPanel && PC.Contents(owner).Count == before - 1);
            History.UndoManager.Undo(); await Frames(3);
            Check("undo restores a prop deleted from its own panel", PC.Contents(owner).Count == before);
            builder.CloseAllPanels();
            racePanel = PC.Open(builder, PC.Find(owner));
            Press(racePanel, props);
            Press(Shown(), goUp);
            await Frames(1);
            Check("going up from a list goes back to the race", Shown() is EditorPanels.ArrayElemEditorPanel);
            builder.CloseAllPanels();

            racePanel = PC.Open(builder, PC.Find(owner));
            OpenOpponents(racePanel, amateur);
            list = (EditorPanels.ContainerContentsPanel)Shown();
            opponent = (CustomElement)Listed(list).First();
            Check("the opponents list has the amateur opponents", opponent.TypeName == "opponent");
            opponent.state.SetInt("difficulty", 1);
            builder.CloseAllPanels();
            racePanel = PC.Open(builder, PC.Find(owner));
            OpenOpponents(racePanel, amateur);
            Check("a professional opponent leaves the amateur list", Listed((EditorPanels.ContainerContentsPanel)Shown()).Count == 0);
            builder.CloseAllPanels();
            racePanel = PC.Open(builder, PC.Find(owner));
            OpenOpponents(racePanel, professional);
            Check("a professional opponent is in the professional list", Listed((EditorPanels.ContainerContentsPanel)Shown()).Contains(opponent));
            Pick((EditorPanels.ContainerContentsPanel)Shown(), opponent);
            await Frames(2);
            Check("the opponent selected in its list is outlined", Outlined(opponent));
            builder.CloseAllPanels();
            opponent.state.SetInt("difficulty", 0);

            History.UndoManager.Record("click opponent", () => builder.SelectObject(opponent));
            await Frames(1);
            Screenshot("race_opponent_back.png");
            Check("an opponent clicked in the city has a back button", ButtonOf(builder.customElementEditorPanel, back) != null);
            Press(builder.customElementEditorPanel, back);
            await Frames(1);
            Check("back from an opponent clicked in the city goes to its difficulty", Shown() is EditorPanels.ArrayElemEditorPanel panel
                && typeof(EditorPanels.ArrayElemEditorPanel).GetField("curState", flags).GetValue(panel) == PC.Find(owner).state.State("policeCars")
                && PC.Active("raceDifficulty")?.state == PC.Find(owner).state.State("policeCars"));
            builder.CloseAllPanels();

            //deleting a checkpoint: from the race panel it goes back to it, clicked in the city it just closes
            race = PC.Find(owner).state;
            var param = TP.GetCustomCityPropertiesTypes()["circuitRace"].parameters.First(p => p.name == "checkpoints");
            History.UndoManager.Record("add checkpoints", () => {
                History.UndoManager.Touch(city);
                Operations.States.AddArrayObjectAt(race, param, new Vector3(-70,0,-70));
                Operations.States.AddArrayObjectAt(race, param, new Vector3(-75,0,-70));
                M.FlagAsChanged();
            });
            M.ProcessUpdate(); await Frames(2);
            race = PC.Find(owner).state;
            var count = race.Array<ObjectState>("checkpoints").Length;
            History.UndoManager.Record("click checkpoint", () => CallBuilder<bool>("SelectCityPropertiesObject", Checkpoint(race.Array<ObjectState>("checkpoints")[count - 1])));
            Check("a checkpoint clicked in the city closes on delete", transformPanel.closeOnDelete);
            Press(transformPanel, StringManager.Get("DELETE"));
            M.ProcessUpdate(); await Frames(2);
            race = PC.Find(owner).state;
            Check("deleting a checkpoint clicked in the city closes every panel", Shown() == null && race.Array<ObjectState>("checkpoints").Length == count - 1);
            Check("deleting a checkpoint clicked in the city leaves no hidden panel", !builder.cityPropertiesEditorPanel.IsOpen());
            Check("deleting a checkpoint keeps the race selected", PC.Active("race")?.state == race);
            racePanel = PC.Open(builder, PC.Find(owner));
            racePanel.OpenArrayElement("checkpoints", 0);
            await Frames(1);
            Check("a checkpoint opened from the race panel goes directly to its transform panel", Shown() == transformPanel && !transformPanel.closeOnDelete);
            var left = race.Array<ObjectState>("checkpoints").Length;
            Press(transformPanel, StringManager.Get("DELETE"));
            M.ProcessUpdate(); await Frames(2);
            race = PC.Find(owner).state;
            Check("deleting a checkpoint opened from the race panel goes back to the checkpoints", Shown() is EditorPanels.ArrayListPanel && race.Array<ObjectState>("checkpoints").Length == left - 1);
            Screenshot("race_checkpoint_deleted.png");
            builder.CloseAllPanels();

            //closing a container closes the panels of what it owns, which gets hidden with it
            var amateurId = PC.EnsureId(PC.Find(owner).state.State("policeCars"));
            void SelectRace(bool difficulty) {
                ArrayObject.activeElements["race"] = PC.Find(owner).state;
                ArrayObject.activeElements["raceDifficulty"] = difficulty ? PC.Find(amateurId).state : null;
                builder.NotifyVisibilityChange(); M.ProcessUpdate();
            }
            void Close(string category) { CallBuilder<object>("DeselectEntity", category); M.ProcessUpdate(); }
            bool AllClosed() => Shown() == null && !builder.cityPropertiesEditorPanel.IsOpen() && builder.gizmo.mainTargetRoot == null;
            void Edit(Node3D node) { History.UndoManager.Record("click element", () => builder.SelectObject(node)); }
            SelectRace(true);
            Edit(opponent);
            await Frames(1);
            Check("an opponent panel opens before closing its difficulty", Shown() == builder.customElementEditorPanel && builder.customElementPlacer.GetElement() == opponent);
            Close("raceDifficulty");
            Check("closing a difficulty closes the panel of its opponent", AllClosed());
            Check("closing a difficulty leaves its race selected", PC.Active("race") != null);
            SelectRace(true);
            Edit(opponent);
            await Frames(1);
            Close("race");
            Check("closing a race closes the panel of an opponent of its difficulty", AllClosed() && PC.Active("race") == null);
            SelectRace(true);
            var ownedLine = PC.Contents(owner).OfType<CustomElement>().First(e => e.TypeName == "propLine");
            Edit(ownedLine);
            await Frames(1);
            Close("raceDifficulty");
            Check("closing a difficulty leaves the panel of a prop line of the race open", Shown() == builder.customElementEditorPanel && builder.customElementPlacer.GetElement() == ownedLine);
            Close("race");
            Check("closing a race closes the panel of its prop line", AllClosed());
            SelectRace(false);
            Edit(ownedLine);
            await Frames(1);
            builder.gizmo.AddTarget(ownedLine.GetAllPoints()[0].GetObject());
            CallBuilder<object>("EditCurSelectedObjectTransform");
            await Frames(1);
            Check("the transform panel of a control point of a prop line opens", Shown() == transformPanel && builder.ControlPointOwnerCustomElement == ownedLine);
            Close("race");
            Check("closing a race closes the transform panel of a control point and the hidden element panel", AllClosed() && !builder.customElementEditorPanel.IsOpen() && builder.ControlPointOwnerCustomElement == null);
            SelectRace(false);
            racePanel = PC.Open(builder, PC.Find(owner));
            Press(racePanel, props);
            list = (EditorPanels.ContainerContentsPanel)Shown();
            Pick(list, Listed(list).OfType<MeshInstance>().First());
            Press(list, StringManager.Get("EDIT"));
            await Frames(1);
            Check("a prop opens from its list before closing the race", Shown() == transformPanel);
            Close("race");
            Check("closing a race closes the panel of its prop and the list it was opened from", AllClosed());
            SelectRace(false);
            racePanel = PC.Open(builder, PC.Find(owner));
            Press(racePanel, props);
            Close("race");
            Check("closing a race closes the list of its contents", AllClosed());
            SelectRace(false);
            History.UndoManager.Record("click checkpoint", () => CallBuilder<bool>("SelectCityPropertiesObject", Checkpoint(PC.Find(owner).state.Array<ObjectState>("checkpoints")[0])));
            await Frames(1);
            Check("a checkpoint panel opens before closing the race", Shown() == transformPanel);
            Close("race");
            Check("closing a race closes the panel of its checkpoint", AllClosed());
            //the other containers keep their panels open
            SelectRace(true);
            Edit(opponent);
            await Frames(1);
            ArrayObject.activeElements["hudmap"] = city.state;
            Close("hudmap");
            Check("closing the hudmap leaves the panel of an opponent open", Shown() == builder.customElementEditorPanel && builder.customElementPlacer.GetElement() == opponent);
            builder.CloseAllPanels();
            SelectRace(true);
            race = PC.Find(owner).state;

            //the layout of the race panels: what it doesn't list can't be edited or selected
            foreach (var typeName in new[] { "circuitRace", "checkpointRace", "blitzRace" }) {
                var type = TP.GetCustomCityPropertiesTypes()[typeName];
                var names = type.uiInfo.Select(e => e.name).ToList();
                var groupNames = type.elementContainer.contentGroups.Select(g => "content:" + g.label).ToList();
                Check(typeName + " lists all its parameters and content groups in its layout", type.parameters.All(p => names.Contains(p.fullName())) && groupNames.All(names.Contains));
                Check(typeName + " lists only what exists in its layout", names.All(n => n.StartsWith("label:") || groupNames.Contains(n) || type.parameters.Any(p => p.fullName() == n)));
            }
            var circuitRace = TP.GetCustomCityPropertiesTypes()["circuitRace"];
            var raceLayout = circuitRace.uiInfo;
            try {
                circuitRace.uiInfo = null;
                racePanel = PC.Open(builder, PC.Find(owner));
                var stacked = Rows(racePanel).Where(r => r.Count > 0).ToList();
                Check("without a layout there is one parameter or list per row, then the end of the panel", stacked.Take(stacked.Count - 1).All(r => r.Count == 1) && stacked.Last().Count == 2);
                Check("without a layout all the parameters and lists are shown", stacked.Count == circuitRace.parameters.Length + circuitRace.elementContainer.contentGroups.Length + 1);
                builder.CloseAllPanels();
                circuitRace.uiInfo = new[] { new CT.TabElement { name = "content:RACE_PROPS", width = 1.5f }, new CT.TabElement { name = "name", width = 1.5f } };
                racePanel = PC.Open(builder, PC.Find(owner));
                await Frames(2);
                Check("a layout can place the props list and omit the difficulty buttons", ButtonOf(racePanel, props) != null
                    && ButtonsOf(racePanel, amateur).Count == 0 && ButtonsOf(racePanel, professional).Count == 0);
                Press(racePanel, props);
                Check("a list placed by a layout opens", Shown() is EditorPanels.ContainerContentsPanel);
            } finally {
                circuitRace.uiInfo = raceLayout;
                builder.CloseAllPanels();
            }

            //the label elements
            float PosOf(EditorPanelElement element, bool y) {
                var pos = (Vector2)typeof(EditorPanelElement).GetField("pos", flags).GetValue(element);
                return y ? pos.Y : pos.X;
            }
            float HeightOf(EditorPanelElement element) => (float)typeof(EditorPanelElement).GetField("rootHeight", flags).GetValue(element);
            var labels = new LabelsPanel();
            try {
                labels.Initialize(builder);
                labels.SetActive(true);
                var rows = Rows(labels).Where(r => r.Count > 0).ToList();
                Check("a label has its own row, with the next elements below it", rows.Count == 8 && rows[0].Single() is EditorPanelElements.Label && rows[1].Count == 2);
                Check("a label shows its string", TitleOf(rows[0][0]).Text == StringManager.Get("RACE_NAME"));
                Check("a label is as tall as the title of a field by default", HeightOf(rows[0][0]) == 20 && PosOf(rows[1][0], true) == 20);
                Check("the row after the fields is at their full height", PosOf(rows[2][0], true) == 70);
                Check("labels can be side by side, the first on the right", rows[2].Count == 2 && rows[2].All(e => e is EditorPanelElements.Label) && TitleOf(rows[2][0]).Text == StringManager.Get("RACE_AMATEUR") && PosOf(rows[2][0], false) == 0 && PosOf(rows[2][1], false) == 120);
                Check("labels side by side use the tallest height", PosOf(rows[3][0], true) == 90);
                Check("a short label row keeps the height of the text, with the next row closer", HeightOf(rows[4][0]) == 20 && PosOf(rows[5][0], true) == PosOf(rows[4][0], true) + 10);
                Check("a tall label fills its row", HeightOf(rows[6][0]) == 40 && PosOf(rows[7][0], true) == PosOf(rows[6][0], true) + 40);
            } finally {
                labels.Destroy();
            }
            var cityPanel = builder.cityPropertiesEditorPanel;
            var cityTab = TP.GetCitySettings()["city"].typeData.GetUI().tabs[0];
            var cityElements = cityTab.elements;
            try {
                TP.GetCitySettings()["city"].typeData.GetUI().tabs[0].elements = new[] { new CT.TabElement { name = "label:RACE_PROPS", width = 1.5f } }.Concat(cityElements).ToArray();
                cityPanel.Initialize(builder);
                cityPanel.SetActive(false);
                var cityRows = Rows(cityPanel); //after the buttons of the tabs
                var labelRow = cityRows.FindIndex(r => r.Count > 0 && r[0] is EditorPanelElements.Label && TitleOf(r[0]).Text == props);
                Check("a label can be in a ui.json", labelRow >= 0 && cityRows[labelRow].Count == 1 && PosOf(cityRows[labelRow + 1][0], true) == PosOf(cityRows[labelRow][0], true) + 20);
            } finally {
                TP.GetCitySettings()["city"].typeData.GetUI().tabs[0].elements = cityElements;
                cityPanel.Initialize(builder);
                cityPanel.SetActive(false);
            }

            //the other race types
            foreach (var (key, withOpponents, inputRows) in new[] { ("checkpointRaces", true, 2), ("blitzRaces", false, 3) }) {
                var typeParam = TP.GetCitySettings()["city"].typeData.parametersInfo.parameters.First(p => p.name == key);
                History.UndoManager.Record("add race", () => {
                    History.UndoManager.Touch(city);
                    Operations.States.AddArrayElement(city.state, typeParam);
                    M.FlagAsChanged();
                });
                M.ProcessUpdate();
                var added = city.state.Array<ObjectState>(key).Last();
                ArrayObject.activeElements["race"] = added;
                builder.NotifyVisibilityChange(); M.ProcessUpdate();
                var addedPanel = PC.Open(builder, PC.Find(PC.EnsureId(added)));
                await Frames(2);
                Screenshot(key + "_panel.png");
                Check(key + " have difficulty subcontainers, props and checkpoints", ButtonsOf(addedPanel, amateur).Count == 1 && ButtonOf(addedPanel, props) != null && ButtonOf(addedPanel, StringManager.Get("CHECKPOINTS")) != null);
                Check(key + " have their values side by side", Rows(addedPanel).Count(r => r.OfType<EditorPanelElements.InputField>().Count() == 2) == inputRows);
                Press(addedPanel, amateur);
                Check(key + " have the opponents only if supported", (ButtonOf(Shown(), StringManager.Get("RACE_OPPONENTS")) != null) == withOpponents);
                builder.CloseAllPanels();
                addedPanel = PC.Open(builder, PC.Find(PC.EnsureId(added)));
                Press(addedPanel, props);
                Check(key + " open their props list", Shown() is EditorPanels.ContainerContentsPanel);
                builder.CloseAllPanels();
            }
            ArrayObject.activeElements["race"] = null;
            builder.NotifyVisibilityChange(); M.ProcessUpdate();
        }
    }
}
