using System;
using System.Collections.Generic;
using System.Runtime.Versioning;

namespace GameTranslator
{
    [SupportedOSPlatform("windows")]
    internal static class PersistentPythonOcrWorkerRegistry
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<PersistentPythonOcrWorkerRegistryKey, RegistryEntry> Entries = new Dictionary<PersistentPythonOcrWorkerRegistryKey, RegistryEntry>();

        public static PersistentPythonOcrWorkerLease Acquire(string engineType, string pythonExecutablePath, string runnerScriptPath)
        {
            if (string.IsNullOrWhiteSpace(engineType))
            {
                throw new ArgumentException("엔진 식별자가 비어 있습니다.", nameof(engineType));
            }

            var key = new PersistentPythonOcrWorkerRegistryKey(engineType, pythonExecutablePath, runnerScriptPath);
            lock (Sync)
            {
                if (!Entries.TryGetValue(key, out RegistryEntry entry))
                {
                    entry = new RegistryEntry(new PersistentPythonOcrWorker(pythonExecutablePath, runnerScriptPath));
                    Entries.Add(key, entry);
                }

                entry.ReferenceCount++;
                return new PersistentPythonOcrWorkerLease(key, entry.Worker, Release);
            }
        }

        private static void Release(PersistentPythonOcrWorkerRegistryKey key)
        {
            PersistentPythonOcrWorker workerToDispose = null;

            lock (Sync)
            {
                if (!Entries.TryGetValue(key, out RegistryEntry entry))
                {
                    return;
                }

                entry.ReferenceCount--;
                if (entry.ReferenceCount > 0)
                {
                    return;
                }

                Entries.Remove(key);
                workerToDispose = entry.Worker;
            }

            workerToDispose?.Dispose();
        }

        internal static int GetEntryCountForTesting()
        {
            lock (Sync)
            {
                return Entries.Count;
            }
        }

        internal static void ResetForTesting()
        {
            List<PersistentPythonOcrWorker> workersToDispose;
            lock (Sync)
            {
                workersToDispose = new List<PersistentPythonOcrWorker>(Entries.Count);
                foreach (RegistryEntry entry in Entries.Values)
                {
                    workersToDispose.Add(entry.Worker);
                }

                Entries.Clear();
            }

            foreach (PersistentPythonOcrWorker worker in workersToDispose)
            {
                worker.Dispose();
            }
        }

        internal readonly struct PersistentPythonOcrWorkerRegistryKey : IEquatable<PersistentPythonOcrWorkerRegistryKey>
        {
            public PersistentPythonOcrWorkerRegistryKey(string engineType, string pythonExecutablePath, string runnerScriptPath)
            {
                EngineType = engineType ?? "";
                PythonExecutablePath = pythonExecutablePath ?? "";
                RunnerScriptPath = runnerScriptPath ?? "";
            }

            public string EngineType { get; }
            public string PythonExecutablePath { get; }
            public string RunnerScriptPath { get; }

            public bool Equals(PersistentPythonOcrWorkerRegistryKey other)
            {
                return string.Equals(EngineType, other.EngineType, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(PythonExecutablePath, other.PythonExecutablePath, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(RunnerScriptPath, other.RunnerScriptPath, StringComparison.OrdinalIgnoreCase);
            }

            public override bool Equals(object obj)
            {
                return obj is PersistentPythonOcrWorkerRegistryKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                var comparer = StringComparer.OrdinalIgnoreCase;
                int hash = comparer.GetHashCode(EngineType);
                hash = (hash * 397) ^ comparer.GetHashCode(PythonExecutablePath);
                hash = (hash * 397) ^ comparer.GetHashCode(RunnerScriptPath);
                return hash;
            }
        }

        private sealed class RegistryEntry
        {
            public RegistryEntry(PersistentPythonOcrWorker worker)
            {
                Worker = worker;
            }

            public PersistentPythonOcrWorker Worker { get; }
            public int ReferenceCount { get; set; }
        }
    }

    [SupportedOSPlatform("windows")]
    internal sealed class PersistentPythonOcrWorkerLease : IDisposable
    {
        private readonly Action<PersistentPythonOcrWorkerRegistry.PersistentPythonOcrWorkerRegistryKey> releaseAction;
        private readonly PersistentPythonOcrWorkerRegistry.PersistentPythonOcrWorkerRegistryKey key;
        private bool disposed;

        internal PersistentPythonOcrWorkerLease(
            PersistentPythonOcrWorkerRegistry.PersistentPythonOcrWorkerRegistryKey key,
            PersistentPythonOcrWorker worker,
            Action<PersistentPythonOcrWorkerRegistry.PersistentPythonOcrWorkerRegistryKey> releaseAction)
        {
            this.key = key;
            this.releaseAction = releaseAction;
            Worker = worker ?? throw new ArgumentNullException(nameof(worker));
        }

        public PersistentPythonOcrWorker Worker { get; }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            releaseAction?.Invoke(key);
        }
    }
}
