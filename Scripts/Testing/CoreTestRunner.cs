using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CoreSelfTests {
    public static class CoreTestRunner {
        public static async Task<int> Run(CityBuilderMenuBar builder, string suite, string folder, string core, string mode = "", string focus = null) {
            folder = Path.GetFullPath(folder);
            var settings = Path.Combine(PathHelper.BasePath(), "settings.json");
            var originalSettings = File.Exists(settings) ? File.ReadAllBytes(settings) : null;
            var context = new CoreTestContext { builder = builder, folder = folder, suite = suite, mode = mode };
            var watch = Stopwatch.StartNew();
            int code = 1;
            Directory.CreateDirectory(Path.GetDirectoryName(folder));
            try {
                var cityPreferences = Path.Combine(folder, "preferences.json");
                var savedCore = File.Exists(cityPreferences) ? JObject.Parse(File.ReadAllText(cityPreferences))["core"]?.Value<string>() : null;
                if (!string.IsNullOrEmpty(savedCore)) {
                    if (core != null && core != savedCore) throw new Cli.UsageException("--core differs from the core of the existing city: " + savedCore);
                    core = savedCore;
                }
                var coresRoot = Path.Combine(PathHelper.BasePath(), "Files", "cores");
                if (core == null) {
                    var installed = CoreManager.GetCores().folders;
                    if (installed.Count != 1) throw new Cli.UsageException("Choose an installed core with --core (installed: " + string.Join(", ", installed) + ")");
                    core = installed[0];
                }
                if (string.IsNullOrWhiteSpace(core) || core != Path.GetFileName(core) || core is "." or ".." || core.Contains('/') || core.Contains('\\'))
                    throw new Cli.UsageException("--core must be an installed core folder name");
                context.core = core;
                context.coreFolder = Path.Combine(coresRoot, core);
                var project = Path.Combine(context.coreFolder, "tests", "SelfTests.csproj");
                if (!File.Exists(project)) throw new Cli.CliError("tests_unavailable", "No developer test project at " + project);
                if (!File.Exists(Path.Combine(PathHelper.BasePath(), "km2 City Builder.csproj")))
                    throw new Cli.CliError("tests_unavailable", "Core self-tests require a builder source checkout and the .NET SDK");
                context.focus = focus ?? Environment.GetEnvironmentVariable("KM2_SELFTEST_FOCUS");
                if (focus == null && string.IsNullOrEmpty(context.focus)) {
                    if (Environment.GetEnvironmentVariable("KM2_SELFTEST_INCREMENTAL") == "1") context.focus = "incremental";
                    else {
                        var selection = Environment.GetEnvironmentVariable("KM2_SELFTEST_MESH_SELECTION");
                        if (!string.IsNullOrEmpty(selection)) context.focus = selection == "benchmark" ? "meshBenchmark" : "meshSelection";
                    }
                }
                var output = Path.Combine(PathHelper.BasePath(), ".godot", "selftests", core);
                var assembly = await Build(context, project, output);
                var loadContext = AssemblyLoadContext.GetLoadContext(typeof(CoreTestRunner).Assembly);
                var loaded = loadContext.LoadFromAssemblyPath(assembly);
                var providers = loaded.GetTypes().Where(t => !t.IsAbstract && typeof(ICoreTestProvider).IsAssignableFrom(t)).ToArray();
                if (providers.Length != 1) throw new InvalidOperationException("The test assembly must contain exactly one ICoreTestProvider");
                var provider = (ICoreTestProvider)Activator.CreateInstance(providers[0]);
                var info = provider.suites.SingleOrDefault(s => s.name == suite);
                if (info == null) throw new Cli.UsageException("The core has no '" + suite + "' suite");
                if (info.modes.Length > 0 && !info.modes.Contains(mode)) throw new Cli.UsageException("--mode is one of: " + string.Join(", ", info.modes));
                if (suite == "api" && !string.IsNullOrEmpty(context.focus) && !info.focuses.Contains(context.focus))
                    throw new Cli.UsageException("--focus is one of: " + string.Join(", ", info.focuses));
                if (info.existingCityModes.Contains(mode) && !File.Exists(Path.Combine(folder, "city.json.gz")))
                    throw new Cli.UsageException("This mode needs a disposable copy of an existing city");
                await provider.RunAsync(context);
                code = context.failures == 0 ? 0 : 1;
            } catch (Cli.UsageException e) {
                context.Log("usage: " + e.Message);
                code = 2;
            } catch (Exception e) {
                context.Log("exception: " + e);
                context.failures++;
            } finally {
                if (originalSettings == null) {
                    if (File.Exists(settings)) File.Delete(settings);
                } else File.WriteAllBytes(settings, originalSettings);
                context.Log("finished in " + watch.Elapsed.TotalSeconds.ToString("0.0") + " s with " + context.failures + " failures");
                File.WriteAllLines(Path.Combine(Path.GetDirectoryName(folder), suite + "_selftest.log"), context.log);
            }
            return code;
        }

        static async Task<string> Build(CoreTestContext context, string project, string output) {
            var host = HostAssemblyDirectory();
            var start = new ProcessStartInfo("dotnet") {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = PathHelper.BasePath()
            };
            foreach (var argument in new[] { "build", project, "--nologo", "-v:q", "-p:BuilderAssemblyDirectory=" + host,
                "-p:BuilderTestSupport=" + Path.Combine(PathHelper.BasePath(), "Tests", "CoreTests.props"),
                "-p:AppendTargetFrameworkToOutputPath=false", "-p:BaseIntermediateOutputPath=" + Path.Combine(output, "obj") + Path.DirectorySeparatorChar,
                "-p:OutputPath=" + Path.Combine(output, "bin") + Path.DirectorySeparatorChar }) start.ArgumentList.Add(argument);
            context.Log("building " + project);
            Process process;
            try { process = Process.Start(start); }
            catch (System.ComponentModel.Win32Exception e) { throw new InvalidOperationException("Core self-tests need the .NET SDK; could not start dotnet", e); }
            using (process) {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                var diagnostics = (await stdout) + (await stderr);
                if (!string.IsNullOrWhiteSpace(diagnostics)) context.Log(diagnostics.TrimEnd());
                if (process.ExitCode != 0) throw new InvalidOperationException("Core test compilation failed (exit " + process.ExitCode + "); no tests were loaded");
            }
            var result = Path.Combine(output, "bin", "CoreSelfTests.dll");
            if (!File.Exists(result)) throw new FileNotFoundException("Core test build did not produce CoreSelfTests.dll", result);
            return result;
        }

        static string HostAssemblyDirectory() {
            var host = typeof(CoreTestRunner).Assembly;
            if (!string.IsNullOrEmpty(host.Location)) return Path.GetDirectoryName(host.Location);
            //Godot can load the project from bytes. Match the running build, including its configuration.
            var bin = Path.Combine(PathHelper.BasePath(), ".godot", "mono", "temp", "bin");
            if (Directory.Exists(bin)) foreach (var directory in Directory.GetDirectories(bin)) {
                var candidate = Path.Combine(directory, host.GetName().Name + ".dll");
                if (!File.Exists(candidate)) continue;
                using var pe = new PEReader(File.OpenRead(candidate));
                var metadata = pe.GetMetadataReader();
                if (metadata.GetGuid(metadata.GetModuleDefinition().Mvid) == host.ManifestModule.ModuleVersionId) return directory;
            }
            throw new InvalidOperationException("Cannot find the running builder's build outputs; build the source checkout before testing");
        }
    }
}
