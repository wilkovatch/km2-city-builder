using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cli {
    //The output of a command: the results go to the standard output (or to the --output file) and nothing else does, the
    //errors and the progress go to the standard error. They are written directly to the streams of the process, so the
    //--quiet option of the engine (which silences its own prints) doesn't hide them.
    public class CliOutput : System.IDisposable {
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        static readonly object errorLock = new object();
        static TextWriter error;

        readonly object outLock = new object();
        readonly TextWriter writer;
        readonly bool pretty;
        readonly bool progress;
        readonly bool toFile;

        public CliOutput(string file, bool pretty, bool progress) {
            this.pretty = pretty;
            this.progress = progress;
            toFile = file != null;
            if (toFile) {
                var dir = Path.GetDirectoryName(Path.GetFullPath(file));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                writer = new StreamWriter(file, false, Utf8);
            } else {
                writer = new StreamWriter(System.Console.OpenStandardOutput(), Utf8) { AutoFlush = true };
            }
        }

        //the standard error of the process (the log of a command line session goes there, see GameEngine.Print)
        public static TextWriter Error {
            get {
                lock (errorLock) {
                    if (error == null) error = TextWriter.Synchronized(new StreamWriter(System.Console.OpenStandardError(), Utf8) { AutoFlush = true });
                    return error;
                }
            }
        }

        public void Line(string text) {
            lock (outLock) {
                writer.WriteLine(text);
                writer.Flush();
            }
        }

        public void Result(JToken value) {
            Line((value ?? JValue.CreateNull()).ToString(pretty ? Formatting.Indented : Formatting.None));
        }

        public static void PrintError(string code, string message) {
            Error.WriteLine("error" + (code != null ? " [" + code + "]" : "") + ": " + message);
        }

        //a progress event of a command (written only with --progress)
        public void Progress(JObject e) {
            if (!progress || e["event"]?.Value<string>() != "progress") return;
            var value = e["value"]?.Value<float>() ?? 0;
            Error.WriteLine("[" + ((int)System.Math.Round(value * 100)).ToString().PadLeft(3) + "%] " + e["text"]?.Value<string>());
        }

        public void Dispose() {
            lock (outLock) {
                writer.Flush();
                if (toFile) writer.Dispose();
            }
        }
    }
}
