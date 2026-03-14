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
            var datRepo = new WorldBuilder.Shared.Services.DatRepositoryService(Microsoft.Extensions.Logging.Abstractions.NullLogger<WorldBuilder.Shared.Services.DatRepositoryService>.Instance);

            var manager = new RecentProjectsManager(settings, NullLogger<RecentProjectsManager>.Instance, datRepo);

            var timeoutTask = Task.Delay(5000);            var completedTask = await Task.WhenAny(manager.InitializationTask, timeoutTask);
            
            Assert.Same(manager.InitializationTask, completedTask);
            Assert.True(manager.InitializationTask.IsCompleted);
        }

        [Fact]
        public async Task AddRecentProject_ResolvesManagedName() {
            var settings = new WorldBuilderSettings();
            var mockDatRepo = new Mock<WorldBuilder.Shared.Services.IDatRepositoryService>();
            var managedId = Guid.NewGuid();
            var managedSet = new WorldBuilder.Shared.Services.ManagedDatSet {
                Id = managedId,
                FriendlyName = "TestManagedSet"
            };

            mockDatRepo.Setup(r => r.GetManagedDataSet(managedId)).Returns(managedSet);

            var manager = new RecentProjectsManager(settings, NullLogger<RecentProjectsManager>.Instance, mockDatRepo.Object);

            await manager.AddRecentProject("client_portal", "some/path/client_portal.dat", true, managedId);

            Assert.Single(manager.RecentProjects);
            Assert.Equal("TestManagedSet", manager.RecentProjects[0].Name);
        }

        public void Dispose() {
            TestSettingsHelper.CleanupTestSettings(_testSettingsDir);
        }
    }
}
