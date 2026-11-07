using System.Collections.Generic;

//The checks of the current core (its checks folder): Python scripts finding the problems of elements of the city that
//the game would have (e.g. the opponents of a race getting stuck), run on the export JSON of the whole city and listed
//like a validation (see Operations.Checks and the Core Checks page of the wiki). Each one is a folder with a check.json:
//{"label": ..., "tooltip": ..., "sources": ["customElement/opponent", "propertyContainer/<type>", ...], "script": "check.py"}
//A check:<name> element in the UI of an accepted type adds its button.
public static class CheckCatalog {
    public class CheckInfo : CoreElementTool {
        public string label;
        public string tooltip;

        public CheckInfo() {
            script = "check.py";
        }

        //a property container (e.g. a race) of the given custom city properties type
        public bool AcceptsContainer(string type) {
            return sources != null && type != null && System.Array.IndexOf(sources, "propertyContainer/" + type) >= 0;
        }
    }

    public static List<CheckInfo> List() => CoreElementTool.Load<CheckInfo>("checks", "check.json");

    public static CheckInfo Get(string name) {
        return List().Find(c => c.name == name);
    }
}
