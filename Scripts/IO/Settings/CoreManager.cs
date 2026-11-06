using LibGit2Sharp;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SM = StringManager;

public class CoreManager {
    public struct CoreInfo {
        public string name;
        public string folder;
        public string repo;
        public bool installed;
        public bool stock;
        public string version;
    }

    //a release of a core, from its release tags (newest first)
    public struct ChangelogEntry {
        public string tag;
        public bool installed; //the release the core is at (or the last one before its current commit)
        public bool newer; //fetched but not installed yet
        public string changes;
    }

    //the update a core can receive: the latest release tag of its branch, or the branch tip for cores without release tags
    struct UpdateInfo {
        public Commit target;
        public bool available;
        public bool blocked; //local changes (or local commits) prevent it
        public bool release;
    }

    //release tags are named like the program's releases: v<version>-<short commit hash>
    static readonly Regex releaseTagRegex = new Regex(@"^v\d+(\.\d+)*-[0-9a-f]{7,}$");
    //paths left out of the installs of cores that don't declare their developerPaths
    static readonly List<string> defaultDeveloperPaths = new List<string> { "tests" };
    //limit for the changelog of a lightweight tag without a previous release (the whole history)
    const int maxChangelogCommits = 100;

    static GenericSettingsManager instance = null;
    static string instancePath;
    static GenericSettingsManager reposInstance = null;

    public static T Get<T>(string key, T defaultValue) {
        return GetInstance().Get(key, defaultValue);
    }

    public static List<T> GetList<T>(string key, List<T> defaultValue) {
        return GetInstance().GetList(key, defaultValue);
    }

    static GenericSettingsManager GetInstance() {
        //read again when the core changed (e.g. read before the city, and so its core, was loaded)
        var path = PathHelper.FilesPath() + "/settings.json";
        if (instance == null || instancePath != path) {
            instance = new GenericSettingsManager(path);
            instance.LoadSettings();
            instancePath = path;
        }
        return instance;
    }

    public static void Reset() {
        instance = null;
    }

    static GenericSettingsManager GetReposInstance() {
        if (reposInstance == null) {
            reposInstance = new GenericSettingsManager(Path.Combine(PathHelper.BasePath(), "coreRepos.json"));
            reposInstance.LoadSettings();
        }
        return reposInstance;
    }

    static int GetSettingsInt(string core, string name) {
        try {
            var fullPath = Path.Combine(PathHelper.BasePath(), "Files", "cores", core) + Path.DirectorySeparatorChar;
            var instance = new GenericSettingsManager(fullPath + "/settings.json");
            instance.LoadSettings();
            return instance.Get(name, -1);
        } catch (System.Exception) {
            return -1;
        }
    }

    public static int GetCoreVersion(string core) {
        return GetSettingsInt(core, "coreVersion");
    }

    public static string ProgramCompatibilityError(string core) {
        var version = GetSettingsInt(core, "programVersion");
        if (version > GlobalVariables.programVersion || version == GlobalVariables.programVersion && GetSettingsInt(core, "featureVersion") > GlobalVariables.featureVersion) return "MIGRATION_ERROR_CORE_IS_FOR_NEWER_PROGRAM";
        return version < GlobalVariables.programVersion ? "MIGRATION_ERROR_CORE_IS_FOR_OLDER_PROGRAM" : null;
    }

    public static int GetCoreFeatureVersion(string core) {
        return GetSettingsInt(core, "coreFeatureVersion");
    }

    public static (List<string> names, List<string> folders, List<CoreInfo> cores) GetCores(bool includeUninstalled = false) {
        var repos = GetReposInstance().GetList("repositories", new List<Dictionary<string, string>>());
        var filesPath = Path.Combine(PathHelper.BasePath(), "Files");
        var dirs = Directory.GetDirectories(Path.Combine(filesPath, "cores"));
        var res1 = new List<string>();
        var res2 = new List<string>();
        var res3 = new List<CoreInfo>();
        var index = new Dictionary<string, int>();
        if (includeUninstalled) {
            foreach (var repo in repos) {
                var elem = new CoreInfo();
                elem.name = repo["name"];
                elem.folder = repo["core"];
                elem.repo = repo["repository"];
                elem.installed = false;
                elem.stock = true;
                res1.Add(elem.name);
                res2.Add(elem.folder);
                res3.Add(elem);
                index[elem.folder] = res1.Count - 1;
            }
        }
        foreach (var dir in dirs) {
            var dirName = Path.GetFileName(dir);
            var settings = new GenericSettingsManager(dir + "/settings.json");
            if (index.ContainsKey(dirName)) {
                var elem = res3[index[dirName]];
                elem.name = SM.Get("CORE_NAME", dir);
                elem.installed = true;
                elem.version = GetCoreRevision(settings, dir);
                res3[index[dirName]] = elem;
            } else {
                settings.LoadSettings();
                var elem = new CoreInfo();
                elem.name = SM.Get("CORE_NAME", dir);
                elem.folder = dirName;
                elem.repo = GetGitRepo(dir);
                elem.installed = true;
                elem.stock = false;
                elem.version = GetCoreRevision(settings, dir);
                res1.Add(elem.name);
                res2.Add(elem.folder);
                res3.Add(elem);
            }
        }
        return (res1, res2, res3);
    }

    public static void AddCore(string repo, System.Action<CoreInfo, bool> post, CityBuilderMenuBar menuBar) {
        var core = new CoreInfo();
        core.repo = repo?.Trim();
        core.folder = GetFolderFromRepo(core.repo);
        if (core.folder == null || Directory.Exists(Path.Combine(PathHelper.FilesPath(true), "cores", core.folder))) {
            if (post != null) post.Invoke(core, false);
            return;
        }
        InstallCore(core, post, menuBar);
    }

    //the folder of a core added by URL is the repository name, e.g. https://host/user/MyCore.git -> MyCore
    static string GetFolderFromRepo(string repo) {
        if (string.IsNullOrEmpty(repo)) return null;
        var name = repo.TrimEnd('/', '\\');
        name = name.Substring(name.LastIndexOfAny(new[] { '/', '\\', ':' }) + 1);
        if (name.EndsWith(".git", System.StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
        if (name == "" || name == "." || name == ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        return name;
    }

    public static void InstallCore(CoreInfo core, System.Action<CoreInfo, bool> post, CityBuilderMenuBar menuBar) {
        var repoPath = Path.Combine(PathHelper.FilesPath(true), "cores", core.folder);

        System.Action action = delegate {
            CloneRelease(core.repo, repoPath, null);
        };

        System.Action<string, string> actionAuth = (username, token) => {
            CloneRelease(core.repo, repoPath, new UsernamePasswordCredentials { Username = username, Password = token });
        };

        DoAuthGitOperation(core, action, actionAuth, post, menuBar);
    }

    static FetchOptions MakeFetchOptions(Credentials cred) {
        var options = new FetchOptions { TagFetchMode = TagFetchMode.All };
        if (cred != null) options.CredentialsProvider = (_url, _user, _cred) => cred;
        return options;
    }

    //clones without checking out, then checks out the latest release (or the branch tip without release tags),
    //leaving out the developer paths declared by that commit's settings
    static void CloneRelease(string url, string repoPath, Credentials cred) {
        var options = new CloneOptions(MakeFetchOptions(cred)) { Checkout = false, RecurseSubmodules = false };
        Repository.Clone(url, repoPath, options);
        using (var repo = new Repository(repoPath)) {
            var tip = ReleaseBranchTip(repo);
            var releases = ReleaseTags(repo, tip);
            var target = releases.Count > 0 ? releases[0].commit : tip;
            WriteSparseConfig(repo, ReadDeveloperPaths(target));
            CheckoutRelease(repo, target, cred);
        }
    }

    static void DoAuthGitOperation(CoreInfo core, System.Action action, System.Action<string, string> actionAuth, System.Action<CoreInfo, bool> post, CityBuilderMenuBar menuBar) {
        try {
            action.Invoke();
        } catch (LibGit2SharpException ex) {
            if (ex.Message.Contains("authentication")) {
                menuBar.CreateInput(SM.Get("PREF_CORES_AUTH_REQUIRED"), SM.Get("PREF_CORES_USERNAME_PH"), SM.Get("CONTINUE"), SM.Get("CANCEL"), username => {
                    menuBar.DoDelayed(delegate {
                        menuBar.CreateInput(SM.Get("PREF_CORES_AUTH_REQUIRED"), SM.Get("PREF_CORES_PASSWORD_PH"), SM.Get("CONTINUE"), SM.Get("CANCEL"), token => {
                            try {
                                actionAuth.Invoke(username, token);
                            } catch (System.Exception ex) {
                                GameEngine.Print(ex.Message);
                                if (post != null) post.Invoke(core, false);
                                return;
                            }
                            if (post != null) post.Invoke(core, true);
                        });
                    });
                });
            } else {
                GameEngine.Print(ex.Message);
                if (post != null) post.Invoke(core, false);
            }
            return;
        } catch (System.Exception ex) {
            //e.g. a malformed repository URL
            GameEngine.Print(ex.Message);
            if (post != null) post.Invoke(core, false);
            return;
        }
        if (post != null) post.Invoke(core, true);
    }

    static string GetGitRepo(string path) {
        try {
            using (var repo = new Repository(path)) {
                return repo.Network.Remotes["origin"].Url;
            }
        } catch (System.Exception) {
            return null;
        }
    }

    static string GetCoreRevision(GenericSettingsManager settings, string path) {
        var version = settings.Get("version", "");
        try {
            using (var repo = new Repository(path)) {
                var head = repo.Head.Tip;
                var commit = head.Sha.Substring(0, 8);
                //the release tag of the current commit, or of the last release before it (e.g. in a developer's clone)
                var releases = ReleaseTags(repo, head);
                if (releases.Count > 0) {
                    var release = releases[0];
                    if (release.commit.Id == head.Id) return release.tag.FriendlyName;
                    var ahead = repo.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = head, ExcludeReachableFrom = release.commit }).Count();
                    return release.tag.FriendlyName + " +" + ahead + " (rev " + commit + ")";
                }
                if (version == "") {
                    return "rev " + commit;
                } else {
                    return version + " (rev " + commit + ")";
                }
            }
        } catch (System.Exception) {
            return version == "" ? "unavailable" : version;
        }
    }

    //the branch releases are taken from: the tracked branch, or the remote's default branch
    static Commit ReleaseBranchTip(Repository repo) {
        var tracked = repo.Head.TrackedBranch;
        if (tracked != null && tracked.Tip != null) return tracked.Tip;
        var originHead = repo.Refs["refs/remotes/origin/HEAD"];
        if (originHead?.ResolveToDirectReference()?.Target is Commit commit) return commit;
        return repo.Head.Tip;
    }

    //the release tags reachable from the given commit, newest first (in topological order, so several
    //releases with the same version are ordered too, and tags outside the branch are ignored)
    static List<(Tag tag, Commit commit)> ReleaseTags(Repository repo, Commit tip) {
        var res = new List<(Tag tag, Commit commit)>();
        if (tip == null) return res;
        var byCommit = new Dictionary<ObjectId, List<Tag>>();
        foreach (var tag in repo.Tags) {
            if (!releaseTagRegex.IsMatch(tag.FriendlyName)) continue;
            if (tag.PeeledTarget is not Commit commit) continue;
            if (!byCommit.TryGetValue(commit.Id, out var list)) byCommit[commit.Id] = list = new List<Tag>();
            list.Add(tag);
        }
        if (byCommit.Count == 0) return res;
        var filter = new CommitFilter { IncludeReachableFrom = tip, SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time };
        foreach (var commit in repo.Commits.QueryBy(filter)) {
            if (!byCommit.TryGetValue(commit.Id, out var tags)) continue;
            foreach (var tag in tags.OrderByDescending(x => x.FriendlyName, System.StringComparer.Ordinal)) res.Add((tag, commit));
            byCommit.Remove(commit.Id);
            if (byCommit.Count == 0) break;
        }
        return res;
    }

    static bool IsAncestor(Repository repo, Commit ancestor, Commit commit) {
        if (ancestor.Id == commit.Id) return true;
        var mergeBase = repo.ObjectDatabase.FindMergeBase(ancestor, commit);
        return mergeBase != null && mergeBase.Id == ancestor.Id;
    }

    //the changes of a release: the message of its annotated tag, or the commit subjects since the previous release
    static string ReleaseChanges(Repository repo, Tag tag, Commit commit, Commit previous) {
        if (tag.IsAnnotated && !string.IsNullOrWhiteSpace(tag.Annotation.Message)) return tag.Annotation.Message.Trim();
        var filter = new CommitFilter { IncludeReachableFrom = commit, SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time };
        if (previous != null) filter.ExcludeReachableFrom = previous;
        var lines = new List<string>();
        foreach (var c in repo.Commits.QueryBy(filter)) {
            if (c.Parents.Count() > 1) continue;
            if (lines.Count == maxChangelogCommits) {
                lines.Add("- ...");
                break;
            }
            lines.Add("- " + c.MessageShort + " (" + c.Sha.Substring(0, 7) + ")");
        }
        return string.Join("\n", lines);
    }

    //the releases of an installed core, including the ones fetched by the last update check but not installed yet
    public static List<ChangelogEntry> GetChangelog(CoreInfo core) {
        var res = new List<ChangelogEntry>();
        try {
            using (var repo = new Repository(GetCorePath(core))) {
                var head = repo.Head.Tip;
                var releases = ReleaseTags(repo, ReleaseBranchTip(repo));
                var installed = head != null ? ReleaseTags(repo, head).Select(x => x.commit).FirstOrDefault() : null;
                for (int i = 0; i < releases.Count; i++) {
                    var (tag, commit) = releases[i];
                    Commit previous = null;
                    for (int j = i + 1; j < releases.Count && previous == null; j++) {
                        if (releases[j].commit.Id != commit.Id) previous = releases[j].commit;
                    }
                    res.Add(new ChangelogEntry {
                        tag = tag.FriendlyName,
                        installed = installed != null && installed.Id == commit.Id,
                        newer = head != null && !IsAncestor(repo, commit, head),
                        changes = ReleaseChanges(repo, tag, commit, previous)
                    });
                }
            }
        } catch (System.Exception) {
            //not a git repository
        }
        return res;
    }

    //the developer paths (left out of the installs) declared by the settings of the given commit
    static List<string> ReadDeveloperPaths(Commit commit) {
        try {
            if (commit?["settings.json"]?.Target is Blob blob) {
                var json = Newtonsoft.Json.Linq.JObject.Parse(blob.GetContentText());
                var paths = json["developerPaths"];
                if (paths != null) return paths.ToObject<List<string>>().Select(NormalizeRelativePath).Where(x => x != null).ToList();
            }
        } catch (System.Exception ex) {
            GameEngine.Print("Invalid developerPaths in the core settings: " + ex.Message);
        }
        return new List<string>(defaultDeveloperPaths);
    }

    //a path relative to the repository root with '/' separators, or null if it could leave the repository
    static string NormalizeRelativePath(string path) {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var parts = path.Replace('\\', '/').Trim('/').Split('/');
        foreach (var part in parts) {
            if (part == "" || part == "." || part == ".." || part.Equals(".git", System.StringComparison.OrdinalIgnoreCase) || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        }
        return string.Join("/", parts);
    }

    //sparse installs are emulated (libgit2 has no sparse checkout) but use git's own settings,
    //so the git command line agrees with them and `git sparse-checkout disable` gives a full checkout back
    static string SparseFile(Repository repo) {
        return Path.Combine(repo.Info.Path, "info", "sparse-checkout");
    }

    //`git sparse-checkout` keeps its settings in config.worktree (with extensions.worktreeConfig), which libgit2 doesn't read
    static string WorktreeConfigFile(Repository repo) {
        return Path.Combine(repo.Info.Path, "config.worktree");
    }

    static bool IsSparse(Repository repo) {
        var worktreeConfig = WorktreeConfigFile(repo);
        if (repo.Config.GetValueOrDefault("extensions.worktreeConfig", false) && File.Exists(worktreeConfig)) {
            using (var config = Configuration.BuildFrom(worktreeConfig)) {
                var entry = config.Get<bool>("core.sparseCheckout", ConfigurationLevel.Local);
                if (entry != null) return entry.Value;
            }
        }
        return repo.Config.GetValueOrDefault("core.sparseCheckout", false);
    }

    //written where `git sparse-checkout init` writes them, so that `git sparse-checkout disable` removes them
    static void WriteSparseConfig(Repository repo, List<string> paths) {
        repo.Config.Set("core.repositoryformatversion", 1);
        repo.Config.Set("extensions.worktreeConfig", true);
        var worktreeConfig = WorktreeConfigFile(repo);
        if (!File.Exists(worktreeConfig)) File.WriteAllText(worktreeConfig, "");
        using (var config = Configuration.BuildFrom(worktreeConfig)) {
            config.Set("core.sparseCheckout", true, ConfigurationLevel.Local);
            config.Set("core.sparseCheckoutCone", false, ConfigurationLevel.Local);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(SparseFile(repo)));
        var lines = new List<string> { "/*" };
        foreach (var path in paths) lines.Add("!/" + path);
        File.WriteAllText(SparseFile(repo), string.Join("\n", lines) + "\n");
    }

    //the paths left out of a sparse install (none for a full checkout)
    static List<string> GetSparseExclusions(Repository repo) {
        var res = new List<string>();
        if (!IsSparse(repo) || !File.Exists(SparseFile(repo))) return res;
        foreach (var line in File.ReadAllLines(SparseFile(repo))) {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("!/")) continue;
            var path = NormalizeRelativePath(trimmed.Substring(2));
            if (path != null) res.Add(path);
        }
        return res;
    }

    static bool IsExcluded(string path, List<string> excluded) {
        path = path.Replace('\\', '/');
        foreach (var e in excluded) {
            if (path == e || path.StartsWith(e + "/")) return true;
        }
        return false;
    }

    static void ApplySparse(Repository repo) {
        var root = Path.GetFullPath(repo.Info.WorkingDirectory);
        if (!root.EndsWith(Path.DirectorySeparatorChar.ToString())) root += Path.DirectorySeparatorChar;
        foreach (var path in GetSparseExclusions(repo)) {
            var full = Path.GetFullPath(Path.Combine(root, path));
            if (!full.StartsWith(root, System.StringComparison.OrdinalIgnoreCase)) continue;
            if (Directory.Exists(full)) {
                ClearReadOnly(full);
                Directory.Delete(full, true);
            } else if (File.Exists(full)) {
                File.SetAttributes(full, FileAttributes.Normal);
                File.Delete(full);
            }
        }
    }

    //changes to the working tree or the index, ignoring the paths left out of a sparse install
    static bool HasLocalChanges(Repository repo) {
        var excluded = GetSparseExclusions(repo);
        var status = repo.RetrieveStatus(new StatusOptions());
        return status.Any(x => x.State != FileStatus.Unaltered && x.State != FileStatus.Ignored && !IsExcluded(x.FilePath, excluded));
    }

    //moves the current branch to the given commit (keeping it attached), then updates the submodules and leaves out the developer paths
    static void CheckoutRelease(Repository repo, Commit commit, Credentials cred) {
        repo.Reset(ResetMode.Hard, commit);
        UpdateSubmodules(repo, cred);
        ApplySparse(repo);
    }

    static void UpdateSubmodules(Repository repo, Credentials cred) {
        var excluded = GetSparseExclusions(repo);
        foreach (Submodule submodule in repo.Submodules) {
            if (IsExcluded(submodule.Path, excluded)) continue;
            var options = new SubmoduleUpdateOptions { Init = true };
            options.FetchOptions.TagFetchMode = TagFetchMode.All;
            if (cred != null) options.FetchOptions.CredentialsProvider = (_url, _user, _cred) => cred;
            repo.Submodules.Update(submodule.Name, options);
            UpdateSubModule(repo, submodule);
        }
    }

    public static string GetCorePath(CoreInfo core) {
        var filesPath = Path.Combine(PathHelper.BasePath(), "Files");
        return Path.Combine(filesPath, "cores", core.folder);
    }

    static void ClearReadOnly(string dir) {
        var dirInfo = new DirectoryInfo(dir);
        if (dirInfo != null) {
            dirInfo.Attributes = FileAttributes.Normal;
            foreach (FileInfo f in dirInfo.GetFiles()) {
                f.Attributes = FileAttributes.Normal;
            }
            foreach (DirectoryInfo d in dirInfo.GetDirectories()) {
                ClearReadOnly(d.FullName);
            }
        }
    }

    public static void UninstallCore(CoreInfo core, System.Action post, System.Action postError) {
        try {
            var path = GetCorePath(core);
            ClearReadOnly(path);
            Directory.Delete(path, true);
            post.Invoke();
        } catch(System.Exception e) {
            GameEngine.Print(e.ToString());
            postError.Invoke();
        }
    }

    public static void CheckUpdates(CoreInfo core, System.Action<CoreInfo, bool, bool> post, CityBuilderMenuBar menuBar) {
        var repoPath = Path.Combine(PathHelper.FilesPath(true), "cores", core.folder);

        System.Action action = delegate {
            Fetch(repoPath, null);
        };

        System.Action<string, string> actionAuth = (username, token) => {
            Fetch(repoPath, new UsernamePasswordCredentials { Username = username, Password = token });
        };

        DoAuthGitOperation(core, action, actionAuth, (x, y) => { CheckLatestCommit(core, post, y); }, menuBar);
    }

    //fetches the branches and all the tags (the releases)
    static void Fetch(string repoPath, Credentials cred) {
        using (var repo = new Repository(repoPath)) {
            var remote = repo.Network.Remotes["origin"];
            var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
            repo.Network.Fetch(remote.Name, refSpecs, MakeFetchOptions(cred));
        }
    }

    static UpdateInfo FindUpdate(Repository repo) {
        var res = new UpdateInfo();
        res.blocked = HasLocalChanges(repo);
        var head = repo.Head.Tip;
        var releases = ReleaseTags(repo, ReleaseBranchTip(repo));
        if (releases.Count > 0) {
            res.release = true;
            var target = releases[0].commit;
            //a commit after the latest release (e.g. in a developer's clone) is not downgraded
            if (head != null && IsAncestor(repo, target, head)) return res;
            res.target = target;
            res.available = true;
            //local commits can't be kept by moving to a release
            if (head != null && !IsAncestor(repo, head, target)) res.blocked = true;
        } else {
            var behindBy = repo.Head.TrackingDetails.BehindBy;
            res.available = behindBy.HasValue && behindBy.Value > 0;
            if (res.available) res.target = repo.Head.TrackedBranch.Tip;
        }
        return res;
    }

    public static bool CheckLatestCommit(CoreInfo core, System.Action<CoreInfo, bool, bool> post, bool fetchOk) {
        if (!fetchOk) {
            if (post != null) post.Invoke(core, false, false);
            return false;
        } else {
            var repoPath = Path.Combine(PathHelper.FilesPath(true), "cores", core.folder);
            try {
                using (var repo = new Repository(repoPath)) {
                    var update = FindUpdate(repo);
                    if (post != null) post.Invoke(core, update.available, update.blocked);
                    return update.available && !update.blocked;
                }
            } catch (System.Exception) {
                if (post != null) post.Invoke(core, false, false);
                return false;
            }
        }
    }

    public static void UpdateCore(CoreInfo core, System.Action<CoreInfo, bool> post, CityBuilderMenuBar menuBar) {
        var repoPath = Path.Combine(PathHelper.FilesPath(true), "cores", core.folder);
        try {
            using (var repo = new Repository(repoPath)) {
                var update = FindUpdate(repo);
                if (!update.available || update.blocked) {
                    if (post != null) post.Invoke(core, false);
                    return;
                }
                var sparse = IsSparse(repo);
                if (update.release) {
                    if (sparse) WriteSparseConfig(repo, ReadDeveloperPaths(update.target));
                    CheckoutRelease(repo, update.target, null);
                } else {
                    //without release tags: merge the tracked branch, with the left out paths restored for the merge
                    var excluded = GetSparseExclusions(repo);
                    if (excluded.Count > 0) repo.CheckoutPaths("HEAD", excluded, new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force });
                    var signature = new Signature("km2cb", "kmc2b", System.DateTimeOffset.UtcNow);
                    repo.Merge(update.target, signature);
                    if (sparse) WriteSparseConfig(repo, ReadDeveloperPaths(repo.Head.Tip));
                    UpdateSubmodules(repo, null);
                    ApplySparse(repo);
                }
            }
            if (post != null) post.Invoke(core, true);
        } catch (System.Exception ex) {
            GameEngine.Print(ex.Message);
            if (post != null) post.Invoke(core, false);
        }
    }

    private static void UpdateSubModule(Repository repo, Submodule submodule) {
        var subrepoPath = Path.Combine(repo.Info.WorkingDirectory, submodule.Path);
        using (Repository subRepo = new Repository(subrepoPath)) {
            string commitHash = submodule.IndexCommitId.ToString();
            Commit commit = subRepo.Lookup<Commit>(commitHash);
            if (commit != null) subRepo.Reset(ResetMode.Hard, commit);
        }
    }
}
