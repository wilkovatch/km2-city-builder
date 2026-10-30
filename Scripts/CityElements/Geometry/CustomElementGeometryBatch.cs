using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RuntimeCalculator.Scripting;

//Only the Lua calculation and decoding run on workers. Constraints, snapshots, rendering, physics and props stay in
//element order on the main thread. Batches bound the memory used by the Lua contexts and the generated geometry.
static class CustomElementGeometryBatch {
    const int BatchSize = 256;
    const int MinParallelCount = 32;

    sealed class Worker : IDisposable {
        readonly Dictionary<string, LuaScriptHost> hosts = new Dictionary<string, LuaScriptHost>();

        public void Calculate(CustomElement.GeometryUpdate job) {
            if (job.type?.geometryFunction == null) return;
            var folder = job.type.scriptHost.folder;
            if (!hosts.TryGetValue(folder, out var host)) {
                host = LuaScriptHost.CreateWorker(folder);
                hosts.Add(folder, host);
            }
            job.Calculate(host);
        }

        public void Dispose() {
            foreach (var host in hosts.Values) host.Dispose();
        }
    }

    public static int Rebuild(List<CustomElement> elements) {
        if (elements.Count < MinParallelCount || Environment.ProcessorCount < 2) {
            var count = 0;
            foreach (var element in elements) count += element.UpdateGeometry();
            return count;
        }
        var workers = new Worker[Math.Min(8, Environment.ProcessorCount - 1)];
        var jobs = new List<CustomElement.GeometryUpdate>(BatchSize);
        var rebuilt = 0;
        void Flush() {
            if (jobs.Count == 0) return;
            if (jobs.Count < MinParallelCount) {
                foreach (var job in jobs) {
                    job.retryOnMainThread = true;
                    job.Apply();
                    rebuilt++;
                }
            } else {
                Parallel.For(0, workers.Length, new ParallelOptions { MaxDegreeOfParallelism = workers.Length }, i => {
                    var worker = workers[i] ??= new Worker();
                    for (int j = i; j < jobs.Count; j += workers.Length) worker.Calculate(jobs[j]);
                });
                foreach (var job in jobs) {
                    job.Apply();
                    rebuilt++;
                }
            }
            jobs.Clear();
        }
        try {
            foreach (var element in elements) {
                //subclasses can change their points or state in Rebuild (e.g. a prop path synchronizing its ribbons)
                if (element.GetType() != typeof(CustomElement) || element.GetCustomElementType()?.canBatchGeometryPreparation == false) {
                    Flush();
                    rebuilt += element.UpdateGeometry();
                    continue;
                }
                var job = element.PrepareGeometryUpdate();
                if (job != null) jobs.Add(job);
                if (jobs.Count == BatchSize) Flush();
            }
            Flush();
            return rebuilt;
        } finally {
            foreach (var worker in workers) worker?.Dispose();
        }
    }
}
