"""Developer integration checks: python Tests/test_core_runner.py --godot <console executable>."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--godot', required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    cores = root / 'Files' / 'cores'
    core = cores / ('zzSelfTestHarness_' + uuid.uuid4().hex)
    output = root / '.godot' / 'selftests' / core.name
    scratch = Path(tempfile.mkdtemp(prefix='runner-checks-', dir=root / '.godot' / 'selftests'))
    settings = root / 'settings.json'
    before = settings.read_bytes() if settings.exists() else None
    env = {key: value for key, value in os.environ.items() if not key.startswith('KM2_SELFTEST_')}
    tests = core / 'tests'
    tests.mkdir(parents=True)
    (core / 'settings.json').write_text('{}', encoding='utf-8')
    (tests / '.gdignore').touch()
    project = tests / 'SelfTests.csproj'
    project.write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net9.0</TargetFramework><AssemblyName>CoreSelfTests</AssemblyName></PropertyGroup>
  <Import Project="$(BuilderTestSupport)" />
</Project>''', encoding='utf-8')
    source = tests / 'Provider.cs'
    source.write_text('''using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CoreSelfTests;
public class Provider : ICoreTestProvider {
    const string marker = "SOURCE_ONE";
    public IReadOnlyList<CoreTestSuiteInfo> suites => new[] {
        new CoreTestSuiteInfo { name = "api", focuses = new[] { "smoke", "throw", "incremental", "meshSelection", "meshBenchmark" } },
        new CoreTestSuiteInfo { name = "undo", modes = new[] { "new", "existing" }, existingCityModes = new[] { "existing" } }
    };
    public async Task RunAsync(CoreTestContext context) {
        if (typeof(CoreTestContext).Assembly.GetType("Api.ApiSelfTest") != null)
            throw new Exception("The builder still contains core tests");
        await context.Frames(2);
        context.Log("MARKER " + marker + " " + context.core);
        context.Log("FOCUS " + context.focus);
        SettingsManager.Set("selftestRunnerFixture", marker);
        if (context.focus == "throw") throw new Exception("fixture exception");
    }
}''', encoding='utf-8')
    checks = 0

    def run(name, suite='api', options=None, expected=0, contains=None, excludes=None, folder=None, environment=None):
        nonlocal checks
        case = scratch / name
        case.mkdir()
        city = folder or case / 'city'
        command = [args.godot, '--path', str(root), '--resolution', '1152x648', '--log-file', str(case / 'godot.log'), '--', 'selftest', suite, str(city)]
        command += options if options is not None else ['--core', core.name, '--focus', 'smoke']
        with (case / 'console.log').open('w', encoding='utf-8') as stream:
            result = subprocess.run(command, cwd=root, env=environment or env, stdout=stream, stderr=subprocess.STDOUT, timeout=60)
        log_path = city.parent / (suite + '_selftest.log')
        assert log_path.exists(), (name, result.returncode, (case / 'console.log').read_text(encoding='utf-8')[-6000:])
        log = log_path.read_text(encoding='utf-8-sig')
        assert result.returncode == expected, (name, result.returncode, log)
        assert contains is None or contains in log, (name, log)
        assert excludes is None or excludes not in log, (name, log)
        assert (settings.read_bytes() if settings.exists() else None) == before, name + ': settings not restored'
        checks += 1
        print('OK ' + name, flush=True)

    try:
        run('cold-build', contains='MARKER SOURCE_ONE')
        source.write_text(source.read_text(encoding='utf-8').replace('SOURCE_ONE', 'SOURCE_TWO'), encoding='utf-8')
        run('source-recompiled', contains='MARKER SOURCE_TWO', excludes='MARKER SOURCE_ONE')
        run('explicit-focus-wins', environment=dict(env, KM2_SELFTEST_FOCUS='unknown', KM2_SELFTEST_INCREMENTAL='1'), contains='FOCUS smoke')
        run('explicit-full-wins', options=['--core', core.name, '--focus', ''], environment=dict(env, KM2_SELFTEST_FOCUS='unknown', KM2_SELFTEST_INCREMENTAL='1'), contains='FOCUS \n')
        run('legacy-focus', options=['--core', core.name], environment=dict(env, KM2_SELFTEST_FOCUS='smoke'), contains='FOCUS smoke')
        run('legacy-incremental', options=['--core', core.name], environment=dict(env, KM2_SELFTEST_INCREMENTAL='1'), contains='FOCUS incremental')
        run('legacy-mesh-selection', options=['--core', core.name], environment=dict(env, KM2_SELFTEST_MESH_SELECTION='test'), contains='FOCUS meshSelection')
        run('legacy-mesh-benchmark', options=['--core', core.name], environment=dict(env, KM2_SELFTEST_MESH_SELECTION='benchmark'), contains='FOCUS meshBenchmark')
        run('unknown-focus', options=['--core', core.name, '--focus', 'unknown'], expected=2, contains='--focus is one of', excludes='MARKER')
        run('unknown-mode', suite='undo', options=['--core', core.name, '--mode', 'unknown'], expected=2, contains='--mode is one of', excludes='MARKER')
        run('ambiguous-core', options=[], expected=2, contains='Choose an installed core', excludes='MARKER')
        run('invalid-core-folder', options=['--core', '..'], expected=2, contains='installed core folder name', excludes='MARKER')
        existing = scratch / 'existing' / 'city'
        existing.mkdir(parents=True)
        (existing / 'preferences.json').write_text(json.dumps({'core': core.name}), encoding='utf-8')
        (existing / 'city.json.gz').touch()
        run('saved-core', suite='undo', options=['--mode', 'existing'], folder=existing, contains='MARKER SOURCE_TWO ' + core.name)
        run('mismatched-core', suite='undo', options=['--core', 'anotherCore', '--mode', 'existing'], folder=existing, expected=2, contains='differs from the core', excludes='MARKER')
        run('missing-city', suite='undo', options=['--core', core.name, '--mode', 'existing'], expected=2, contains='disposable copy', excludes='MARKER')
        run('suite-exception', options=['--core', core.name, '--focus', 'throw'], expected=1, contains='fixture exception')
        off = project.with_suffix('.off')
        project.rename(off)
        try:
            run('missing-project', expected=1, contains='No developer test project', excludes='MARKER')
        finally:
            off.rename(project)
        broken = tests / 'Broken.cs'
        broken.write_text('this is not C#;', encoding='utf-8')
        try:
            assert (output / 'bin' / 'CoreSelfTests.dll').exists()
            run('failed-build-rejects-stale-binary', expected=1, contains='compilation failed', excludes='MARKER')
        finally:
            broken.unlink()
        no_sdk = dict(env, PATH=str(scratch))
        run('missing-sdk', environment=no_sdk, expected=1, contains='could not start dotnet', excludes='MARKER')
        print(str(checks) + ' runner checks passed; logs: ' + str(scratch), flush=True)
    finally:
        #Only directories created by this invocation, inside the known checkout, may be removed.
        assert core.parent.resolve() == cores.resolve() and core.name.startswith('zzSelfTestHarness_')
        shutil.rmtree(core)
        if output.exists():
            assert output.parent.resolve() == (root / '.godot' / 'selftests').resolve()
            shutil.rmtree(output)
        if before is None:
            settings.unlink(missing_ok=True)
        else:
            settings.write_bytes(before)


if __name__ == '__main__':
    main()
