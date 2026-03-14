using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.IO;
using System.Threading.Tasks;
using WorldBuilder.Services;
using WorldBuilder.Shared.Tests.Helpers;
using Xunit;

namespace WorldBuilder.Tests.Services {
    public class RecentProjectsManagerTests : IDisposable {
        private readonly string _testSettingsDir;

        public RecentProjectsManagerTests() {
            _testSettingsDir = TestSettingsHelper.SetupTestSettings();
        }

        [Fact]
        public async Task InitializationTask_CompletesAfterLoading() {
            var settings = new WorldBuilderSettings();
            
            var manager = new RecentProjectsManager(settings, NullLogger<RecentProjectsManager>.Instance);
            
            var timeoutTask = Task.Delay(5000);
            var completedTask = await Task.WhenAny(manager.InitializationTask, timeoutTask);
            
            Assert.Same(manager.InitializationTask, completedTask);
            Assert.True(manager.InitializationTask.IsCompleted);
        }

        public void Dispose() {
            TestSettingsHelper.CleanupTestSettings(_testSettingsDir);
        }
    }
}
