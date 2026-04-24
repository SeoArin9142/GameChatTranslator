using GameTranslator;
using System.Runtime.Versioning;
using Xunit;

namespace GameChatTranslator.Tests
{
    [SupportedOSPlatform("windows")]
    public class PersistentPythonOcrWorkerRegistryTests : System.IDisposable
    {
        public PersistentPythonOcrWorkerRegistryTests()
        {
            PersistentPythonOcrWorkerRegistry.ResetForTesting();
        }

        public void Dispose()
        {
            PersistentPythonOcrWorkerRegistry.ResetForTesting();
        }

        [Fact]
        public void Acquire_SameKey_ReusesSingleWorkerEntry()
        {
            using PersistentPythonOcrWorkerLease firstLease =
                PersistentPythonOcrWorkerRegistry.Acquire("easyocr_runner.py", "python", "easyocr_runner.py");
            using PersistentPythonOcrWorkerLease secondLease =
                PersistentPythonOcrWorkerRegistry.Acquire("easyocr_runner.py", "python", "easyocr_runner.py");

            Assert.Same(firstLease.Worker, secondLease.Worker);
            Assert.Equal(1, PersistentPythonOcrWorkerRegistry.GetEntryCountForTesting());
        }

        [Fact]
        public void Acquire_DifferentEngineKeys_CreatesSeparateEntries()
        {
            using PersistentPythonOcrWorkerLease easyLease =
                PersistentPythonOcrWorkerRegistry.Acquire("easyocr_runner.py", "python", "easyocr_runner.py");
            using PersistentPythonOcrWorkerLease paddleLease =
                PersistentPythonOcrWorkerRegistry.Acquire("paddleocr_runner.py", "python", "paddleocr_runner.py");

            Assert.NotSame(easyLease.Worker, paddleLease.Worker);
            Assert.Equal(2, PersistentPythonOcrWorkerRegistry.GetEntryCountForTesting());
        }

        [Fact]
        public void Dispose_LastLease_RemovesRegistryEntry()
        {
            PersistentPythonOcrWorkerLease firstLease =
                PersistentPythonOcrWorkerRegistry.Acquire("easyocr_runner.py", "python", "easyocr_runner.py");
            PersistentPythonOcrWorkerLease secondLease =
                PersistentPythonOcrWorkerRegistry.Acquire("easyocr_runner.py", "python", "easyocr_runner.py");

            firstLease.Dispose();
            Assert.Equal(1, PersistentPythonOcrWorkerRegistry.GetEntryCountForTesting());

            secondLease.Dispose();
            Assert.Equal(0, PersistentPythonOcrWorkerRegistry.GetEntryCountForTesting());
        }
    }
}
