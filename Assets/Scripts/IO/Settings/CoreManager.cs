using LibGit2Sharp;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
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

    static GenericSettingsManager instance = null;
    static GenericSettingsManager reposInstance = null;

    public static T Get<T>(string key, T defaultValue) {
        return GetInstance().Get(key, defaultValue);
    }

    public static List<T> GetList<T>(string key, List<T> defaultValue) {
        return GetInstance().GetList(key, defaultValue);
    }

    static GenericSettingsManager GetInstance() {
        if (instance == null) {
            instance = new GenericSettingsManager(PathHelper.FilesPath() + "/settings.json");
            instance.LoadSettings();
        }
        return instance;
    }

    public static void Reset() {
        instance = null;
    }

    static GenericSettingsManager GetReposInstance() {
        if (reposInstance == null) {
            reposInstance = new GenericSettingsManager(new DirectoryInfo(Application.dataPath).Parent + "/coreRepos.json");
            reposInstance.LoadSettings();
        }
        return reposInstance;
    }

    static int GetSettingsInt(string core, string name) {
        try {
            var path = new DirectoryInfo(Application.dataPath);
            var fullPath = Path.Combine(path.Parent.ToString(), "Files", "cores", core) + "/"; //TODO: path.combine only if possible
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

    public static int GetCoreFeatureVersion(string core) {
        return GetSettingsInt(core, "coreFeatureVersion");
    }

    public static (List<string> names, List<string> folders, List<CoreInfo> cores) GetCores(bool includeUninstalled = false) {
        var repos = GetReposInstance().GetList("repositories", new List<Dictionary<string, string>>());
        var path = new DirectoryInfo(Application.dataPath);
        var filesPath = Path.Combine(path.Parent.ToString(), "Files");
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
        core.repo = repo;
        InstallCore(core, post, menuBar);
    }

    public static void InstallCore(CoreInfo core, System.Action<CoreInfo, bool> post, CityBuilderMenuBar menuBar) {
        var repoPath = Path.Combine(PathHelper.FilesPath(true), "cores", core.folder);

        System.Action action = delegate {
            var options = new CloneOptions();
            options.RecurseSubmodules = true;
            Repository.Clone(core.repo, repoPath, options);
        };

        System.Action<string, string> actionAuth = (username, token) => {
            var options = new CloneOptions();
            var cred = new UsernamePasswordCredentials { Username = username, Password = token };
            options.FetchOptions.CredentialsProvider = (_url, _user, _cred) => cred;
            options.RecurseSubmodules = true;
            Repository.Clone(core.repo, repoPath, options);
        };

        DoAuthGitOperation(core, action, actionAuth, post, menuBar);
    }

    static void DoAuthGitOperation(CoreInfo core, System.Action action, System.Action<string, string> actionAuth, System.Action<CoreInfo, bool> post, CityBuilderMenuBar menuBar) {
        try {
            action.Invoke();
            if (post != null) post.Invoke(core, true);
        } catch (LibGit2SharpException ex) {
            if (ex.Message.Contains("authentication")) {
                menuBar.CreateInput(SM.Get("PREF_CORES_AUTH_REQUIRED"), SM.Get("PREF_CORES_USERNAME_PH"), SM.Get("CONTINUE"), SM.Get("CANCEL"), username => {
                    menuBar.DoDelayed(delegate {
                        menuBar.CreateInput(SM.Get("PREF_CORES_AUTH_REQUIRED"), SM.Get("PREF_CORES_PASSWORD_PH"), SM.Get("CONTINUE"), SM.Get("CANCEL"), token => {
                            try {
                                actionAuth.Invoke(username, token);
                                if (post != null) post.Invoke(core, true);
                            } catch (LibGit2SharpException ex) {
                                MonoBehaviour.print(ex.Message);
                                if (post != null) post.Invoke(core, false);
                            }
                        });
                    });
                });
            } else {
                MonoBehaviour.print(ex.Message);
                if (post != null) post.Invoke(core, false);
            }
        }
    }

    static string GetGitRepo(string path) {
        try {
            var repo = new Repository(path);
            return repo.Network.Remotes["origin"].Url;
        } catch (System.Exception) {
            return null;
        }
    }

    static string GetCoreRevision(GenericSettingsManager settings, string path) {
        var version = settings.Get("version", "");
        try {
            var repo = new Repository(path);
            var commit = repo.Head.Tip.Id.Sha.Substring(0, 8);
            if (version == "") {
                return "rev " + commit;
            } else {
                return version + " (rev " + commit + ")";
            }
        } catch (System.Exception) {
            return version == "" ? "unavailable" : version;
        }
    }

    public static string GetCorePath(CoreInfo core) {
        var path = new DirectoryInfo(Application.dataPath);
        var filesPath = Path.Combine(path.Parent.ToString(), "Files");
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
            MonoBehaviour.print(e);
            postError.Invoke();
        }
    }

    public static void CheckUpdates(CoreInfo core, System.Action<CoreInfo, bool, bool> post, CityBuilderMenuBar menuBar) {
        var repoPath = Path.Combine(PathHelper.FilesPath(true), "cores", core.folder);

        System.Action action = delegate {
            var repo = new Repository(repoPath);
            var options = new FetchOptions();
            var remote = repo.Network.Remotes["origin"];
            var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
            repo.Network.Fetch(remote.Name, refSpecs, options);
        };

        System.Action<string, string> actionAuth = (username, token) => {
            var repo = new Repository(repoPath);
            var options = new FetchOptions();
            var cred = new UsernamePasswordCredentials { Username = username, Password = token };
            options.CredentialsProvider = (_url, _user, _cred) => cred;
            var remote = repo.Network.Remotes["origin"];
            var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
            repo.Network.Fetch(remote.Name, refSpecs, options);
        };

        DoAuthGitOperation(core, action, actionAuth, (x, y) => { CheckLatestCommit(core, post, y); }, menuBar);
    }

    public static bool CheckLatestCommit(CoreInfo core, System.Action<CoreInfo, bool, bool> post, bool fetchOk) {
        if (!fetchOk) {
            if (post != null) post.Invoke(core, false, false);
            return false;
        } else {
            var repoPath = Path.Combine(PathHelper.FilesPath(true), "cores", core.folder);
            try {
                var repo = new Repository(repoPath);
                var status = repo.RetrieveStatus();
                var dirty = repo.Diff.Compare<TreeChanges>().Count > 0 || status.Untracked.Count() > 0;
                var behindBy = repo.Head.TrackingDetails.BehindBy;
                var newCount = behindBy.HasValue ? behindBy.Value : 0;
                var updateAvailable = newCount > 0;
                if (post != null) post.Invoke(core, updateAvailable, dirty);
                return updateAvailable && !dirty;
            } catch (System.Exception) {
                if (post != null) post.Invoke(core, false, false);
                return false;
            }
        }
    }

    public static void UpdateCore(CoreInfo core, System.Action<CoreInfo, bool> post, CityBuilderMenuBar menuBar) {
        var updateAvailable = CheckLatestCommit(core, null, true);
        var repoPath = Path.Combine(PathHelper.FilesPath(true), "cores", core.folder);
        if (updateAvailable) {
            try {
                var repo = new Repository(repoPath);
                var signature = new Signature("km2cb", "kmc2b", System.DateTimeOffset.UtcNow);
                repo.Merge(repo.Head.TrackedBranch.Tip, signature);
                foreach (Submodule submodule in repo.Submodules) {
                    UpdateSubModule(repo, submodule);
                }
                if (post != null) post.Invoke(core, true);
            } catch (System.Exception ex) {
                MonoBehaviour.print(ex.Message);
                if (post != null) post.Invoke(core, false);
            }
        } else {
            if (post != null) post.Invoke(core, false);
        }
    }

    private static void UpdateSubModule(Repository repo, Submodule submodule) {
        var subrepoPath = Path.Combine(repo.Info.WorkingDirectory, submodule.Path);
        using (Repository subRepo = new Repository(subrepoPath)) {
            string commitHash = submodule.IndexCommitId.ToString();
            Commit commit = subRepo.Lookup<Commit>(commitHash);
            subRepo.Reset(ResetMode.Hard, commit);
        }
    }
}
