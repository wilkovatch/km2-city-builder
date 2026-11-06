using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace CoreSelfTests {
    //The only contract between the builder and developer-only test assemblies owned by cores.
    public interface ICoreTestProvider {
        IReadOnlyList<CoreTestSuiteInfo> suites { get; }
        Task RunAsync(CoreTestContext context);
    }

    public class CoreTestSuiteInfo {
        public string name;
        public string[] modes = System.Array.Empty<string>();
        public string[] focuses = System.Array.Empty<string>();
        public string[] existingCityModes = System.Array.Empty<string>();
    }

    public class CoreTestContext {
        public CityBuilderMenuBar builder;
        public string core, coreFolder, folder, suite, mode, focus;
        public int failures;
        public readonly List<string> log = new();

        public void Log(string line) {
            log.Add(line);
            GD.Print("[" + suite + "-selftest] " + line);
        }

        public async Task Frames(int count) {
            for (int i = 0; i < count; i++) await builder.ToSignal(builder.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
