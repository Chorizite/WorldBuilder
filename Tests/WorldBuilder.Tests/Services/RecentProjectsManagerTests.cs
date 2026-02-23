using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.IO;
using System.Threading.Tasks;
using WorldBuilder.Services;
using Xunit;

namespace WorldBuilder.Tests.Services {
    public class RecentProjectsManagerTests : IDisposable {
        private readonly string _testDataDir;

        public RecentProjectsManagerTests() {
            _testDataDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDataDir);
        }

        [Fact]
        public async Task InitializationTask_CompletesAfterLoading() {
            var settings = new WorldBuilderSettings();
            // Note: This will use the real AppDataDirectory, which is not great for tests
            // but for verifying the task completion it should be fine.
            
            var manager = new RecentProjectsManager(settings, NullLogger<RecentProjectsManager>.Instance);
            
            var timeoutTask = Task.Delay(5000);
            var completedTask = await Task.WhenAny(manager.InitializationTask, timeoutTask);
            
            Assert.Same(manager.InitializationTask, completedTask);
            Assert.True(manager.InitializationTask.IsCompleted);
        }

        public void Dispose() {
            if (Directory.Exists(_testDataDir)) {
                Directory.Delete(_testDataDir, true);
            }
        }
    }
}
