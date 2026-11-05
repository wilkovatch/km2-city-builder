using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SM = StringManager;

//A guide: a folder with a guide.json (title, order, list of chapters) and a Markdown file for each chapter,
//in guides/<language>/<guide id>/ of the program (Files) or of a core. See the wiki page Core-Guides.
public class Guide {
    public class Chapter {
        public string id;
        public string file;
        public string title;
    }

    public string id;
    public string title;
    public string folder;
    public int order;
    public List<Chapter> chapters = new List<Chapter>();
}

//The guides of the program or of a core
public class GuideGroup {
    public string name; //null for the guides of the program
    public string folder; //the core folder, null for the guides of the program
    public List<Guide> guides = new List<Guide>();
}

//Finds the guides on disk. It reads the folders of all the installed cores, so it works without a city and doesn't
//depend on the current core (CoreManager.Get can't be used before a city is loaded). Nothing is cached: it runs when
//the Help menu opens, and the cores can be installed and uninstalled while the program is running.
public static class GuideLibrary {
    const string DefaultLanguage = "en-US";
    const string GuideFile = "guide.json";

    public static List<GuideGroup> Load() {
        var res = new List<GuideGroup>();
        var files = Path.Combine(PathHelper.BasePath(), "Files");
        var own = new GuideGroup { guides = LoadRoot(files) };
        if (own.guides.Count > 0) res.Add(own);
        var cores = Path.Combine(files, "cores");
        if (!Directory.Exists(cores)) return res;
        foreach (var dir in Directory.GetDirectories(cores).OrderBy(d => d, System.StringComparer.OrdinalIgnoreCase)) {
            var guides = LoadRoot(dir);
            if (guides.Count == 0) continue;
            var folder = Path.GetFileName(dir);
            res.Add(new GuideGroup { name = SM.Get("CORE_NAME", dir) ?? folder, folder = folder, guides = guides });
        }
        return res;
    }

    //the guides of guides/<language> in the folder, or of the default language if it has none
    static List<Guide> LoadRoot(string root) {
        var res = new List<Guide>();
        var languages = Path.Combine(root, "guides");
        if (!Directory.Exists(languages)) return res;
        var folder = Path.Combine(languages, SettingsManager.Get("Language", DefaultLanguage));
        if (!Directory.Exists(folder)) folder = Path.Combine(languages, DefaultLanguage);
        if (!Directory.Exists(folder)) return res;
        foreach (var dir in Directory.GetDirectories(folder)) {
            var guide = LoadGuide(dir);
            if (guide != null) res.Add(guide);
        }
        return res.OrderBy(g => g.order).ThenBy(g => g.title, System.StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    //the guide in the folder, null (with a warning) if it isn't valid
    public static Guide LoadGuide(string folder) {
        var file = Path.Combine(folder, GuideFile);
        try {
            if (!File.Exists(file)) {
                GameEngine.LogWarning("Guide without " + GuideFile + ": " + folder);
                return null;
            }
            var json = JObject.Parse(File.ReadAllText(file));
            var guide = new Guide {
                id = Path.GetFileName(folder.TrimEnd('/', '\\')),
                title = (string)json["title"],
                order = (int?)json["order"] ?? 0,
                folder = Path.GetFullPath(folder)
            };
            if (string.IsNullOrEmpty(guide.title)) guide.title = guide.id;
            foreach (var chapter in json["chapters"] ?? new JArray()) {
                var id = (string)chapter;
                var path = Path.Combine(guide.folder, id + ".md");
                guide.chapters.Add(new Guide.Chapter { id = id, file = path, title = ReadTitle(path) ?? id });
            }
            if (guide.chapters.Count == 0) {
                GameEngine.LogWarning("Guide without chapters: " + file);
                return null;
            }
            return guide;
        } catch (System.Exception e) {
            GameEngine.LogWarning("Unable to read the guide " + file + ": " + e.Message);
            return null;
        }
    }

    //the first level 1 heading of the chapter
    static string ReadTitle(string file) {
        if (!File.Exists(file)) return null;
        foreach (var line in File.ReadLines(file)) {
            if (line.StartsWith("# ")) return line.Substring(2).Trim();
        }
        return null;
    }

    //the Markdown of a chapter, null (with a warning) if it can't be read
    public static string ReadChapter(Guide.Chapter chapter) {
        try {
            return File.ReadAllText(chapter.file);
        } catch (System.Exception e) {
            GameEngine.LogWarning("Unable to read the guide chapter " + chapter.file + ": " + e.Message);
            return null;
        }
    }

    //Only web links open outside of the program: the guides of a core come from its repository, and
    //OS.ShellOpen would open (or run) any other path or scheme given to it.
    public static bool IsWebLink(string href) {
        return System.Uri.TryCreate(href, System.UriKind.Absolute, out var uri)
            && (uri.Scheme == System.Uri.UriSchemeHttp || uri.Scheme == System.Uri.UriSchemeHttps);
    }

    //The chapter a relative link leads to (e.g. "roads.md" or "../advanced/roads.md"), of the same guide or
    //of another one in the groups. Returns false if there is no such chapter.
    public static bool ResolveChapter(List<GuideGroup> groups, Guide from, string href, out Guide guide, out int chapter) {
        guide = null;
        chapter = -1;
        if (string.IsNullOrEmpty(href) || IsWebLink(href) || !href.EndsWith(".md", System.StringComparison.OrdinalIgnoreCase)) return false;
        string path;
        try {
            path = Path.GetFullPath(Path.Combine(from.folder, href));
        } catch (System.Exception) {
            return false;
        }
        foreach (var candidate in groups.SelectMany(g => g.guides).Prepend(from)) {
            var index = candidate.chapters.FindIndex(c => string.Equals(Path.GetFullPath(c.file), path, System.StringComparison.OrdinalIgnoreCase));
            if (index < 0) continue;
            guide = candidate;
            chapter = index;
            return true;
        }
        return false;
    }

    //The path of an image of the guide, null if it is outside of the folder of the guide or if it doesn't exist.
    public static string ResolveImage(Guide guide, string src) {
        if (string.IsNullOrEmpty(src)) return null;
        try {
            var root = Path.GetFullPath(guide.folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(Path.Combine(root, src));
            if (!path.StartsWith(root, System.StringComparison.OrdinalIgnoreCase)) return null;
            return File.Exists(path) ? path : null;
        } catch (System.Exception) {
            return null;
        }
    }
}
