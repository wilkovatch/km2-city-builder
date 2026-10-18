using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Api {
    //The Python script being run (see ScriptRunner): its inputs, and its progress and messages shown by the program
    public partial class CityApi {
        static ScriptRunner RequireScript() {
            var res = ScriptRunner.Current;
            if (res == null) throw ApiException.InvalidOperation("no script is running");
            return res;
        }

        [ApiMethod("script.inputs", "The values entered in the input panel of the running script, by parameter name (enums as indices, arrays as lists of objects)")]
        public JToken ScriptInputs() {
            return RequireScript().inputs.DeepClone();
        }

        [ApiMethod("script.selection", "The ids of the elements selected or being edited when the running script was started")]
        public List<int> ScriptSelection() {
            return new List<int>(RequireScript().selection);
        }

        [ApiMethod("script.progress", "Shows the progress of the running script (value from 0 to 1), with a text (null: keeps the last one)")]
        public void ScriptProgress(float value, string text = null) {
            RequireScript().SetProgress(value, text);
        }

        [ApiMethod("script.log", "Writes a line in the log of the program; level: info, warning or error")]
        public void ScriptLog(string text, string level = "info") {
            var script = RequireScript();
            var line = "[" + script.script.label + "] " + text;
            switch (level) {
                case "info": GameEngine.Print(line); break;
                case "warning": GameEngine.LogWarning(line); break;
                case "error": GameEngine.LogError(line); break;
                default: throw ApiException.InvalidValue("level must be info, warning or error");
            }
        }

        [ApiMethod("script.alert", "Shows a message when the running script ends")]
        public void ScriptAlert(string text, string title = null) {
            RequireScript().AddMessage(title, text);
        }
    }
}
