using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.IO;
using System.Threading.Tasks;
using WorldBuilder.Services;
using WorldBuilder.Shared.Tests.Helpers;
using Xunit;

namespace WorldBuilder.Tests.Services {
    [Collection("StaticSettingsTests")]
    public class RecentProjectsManagerTests : IDisposable {
        private readonly string _testSettingsDir;

        public RecentProjectsManagerTests() {
            _testSettingsDir = TestSettingsHelper.SetupTestSettings();
        }

        [Fact]
        public async Task InitializationTask_CompletesAfterLoading() {
            var settings = new WorldBuilderSettings();
            var datRepo = new WorldBuilder.Shared.Services.DatRepositoryService(Microsoft.Extensions.Logging.Abstractions.NullLogger<WorldBuilder.Shared.Services.DatRepositoryService>.Instance);
            var aceRepo = new WorldBuilder.Shared.Services.AceRepositoryService(Microsoft.Extensions.Logging.Abstractions.NullLogger<WorldBuilder.Shared.Services.AceRepositoryService>.Instance, new System.Net.Http.HttpClient());

            var manager = new RecentProjectsManager(settings, NullLogger<RecentProjectsManager>.Instance, datRepo, aceRepo);

            var timeoutTask = Task.Delay(5000);            var completedTask = await Task.WhenAny(manager.InitializationTask, timeoutTask);
            
            Assert.Same(manager.InitializationTask, completedTask);
            Assert.True(manager.InitializationTask.IsCompleted);
        }

        [Fact]
        public async Task AddRecentProject_ResolvesManagedName() {
            var settings = new WorldBuilderSettings();
            var mockDatRepo = new Mock<WorldBuilder.Shared.Services.IDatRepositoryService>();
            var mockAceRepo = new Mock<WorldBuilder.Shared.Services.IAceRepositoryService>();
            var managedId = Guid.NewGuid();
            var managedSet = new WorldBuilder.Shared.Services.ManagedDatSet {
                Id = managedId,
                FriendlyName = "TestManagedSet"
            };

            mockDatRepo.Setup(r => r.GetManagedDataSet(managedId)).Returns(managedSet);

            var manager = new RecentProjectsManager(settings, NullLogger<RecentProjectsManager>.Instance, mockDatRepo.Object, mockAceRepo.Object);

            await manager.AddRecentProject("client_portal", "some/path/client_portal.dat", true, managedId);

            Assert.Single(manager.RecentProjects);
            Assert.Equal("TestManagedSet", manager.RecentProjects[0].Name);
        }

        [Fact]
        public async Task RecentProjects_Verify_RespectsRepositoryRoot() {
            var settings = new WorldBuilderSettings();
            var mockDatRepo = new Mock<WorldBuilder.Shared.Services.IDatRepositoryService>();
            var mockAceRepo = new Mock<WorldBuilder.Shared.Services.IAceRepositoryService>();
            
            var managedId = Guid.NewGuid();
            var managedSet = new WorldBuilder.Shared.Services.ManagedDatSet {
                Id = managedId,
                FriendlyName = "TestManagedSet"
            };

            mockDatRepo.Setup(r => r.GetManagedDataSet(managedId)).Returns(managedSet);

            var manager = new RecentProjectsManager(settings, NullLogger<RecentProjectsManager>.Instance, mockDatRepo.Object, mockAceRepo.Object);
            await manager.InitializationTask;

            // Create a fake project file
            var projectFile = Path.Combine(_testSettingsDir, "test.wbproj");
            File.WriteAllText(projectFile, "{}");

            await manager.AddRecentProject("Test Project", projectFile, false, managedId);

            Assert.Single(manager.RecentProjects);
            Assert.False(manager.RecentProjects[0].HasError);
            Assert.Equal(managedId, manager.RecentProjects[0].ManagedDatId);
            
            // Now verify that if it's NOT in the repo, it shows an error
            var otherManagedId = Guid.NewGuid();
            await manager.AddRecentProject("Project With Missing DAT", projectFile, false, otherManagedId);
            
            Assert.True(manager.RecentProjects[0].HasError);
            Assert.Equal("Managed DAT set no longer exists", manager.RecentProjects[0].Error);
        }

        [Fact]
        public async Task RecentProjects_Verify_MissingAceDb_NoErrorMessage() {
            var settings = new WorldBuilderSettings();
            var mockDatRepo = new Mock<WorldBuilder.Shared.Services.IDatRepositoryService>();
            var mockAceRepo = new Mock<WorldBuilder.Shared.Services.IAceRepositoryService>();

            var managedDatId = Guid.NewGuid();
            var managedAceId = Guid.NewGuid();
            var managedSet = new WorldBuilder.Shared.Services.ManagedDatSet {
                Id = managedDatId,
                FriendlyName = "TestManagedSet"
            };

            mockDatRepo.Setup(r => r.GetManagedDataSet(managedDatId)).Returns(managedSet);
            // Missing ACE DB setup should return null by default for mocks, but let's be explicit
            mockAceRepo.Setup(r => r.GetManagedAceDb(managedAceId)).Returns((WorldBuilder.Shared.Services.ManagedAceDb?)null);

            var manager = new RecentProjectsManager(settings, NullLogger<RecentProjectsManager>.Instance, mockDatRepo.Object, mockAceRepo.Object);
            await manager.InitializationTask;

            // Create a fake project file
            var projectFile = Path.Combine(_testSettingsDir, "test_ace.wbproj");
            File.WriteAllText(projectFile, "{}");

            await manager.AddRecentProject("Test Project", projectFile, false, managedDatId, managedAceId);

            Assert.Single(manager.RecentProjects);
            Assert.False(manager.RecentProjects[0].HasError);
            Assert.Equal(managedAceId, manager.RecentProjects[0].ManagedAceId);
        }

        public void Dispose() {
            TestSettingsHelper.CleanupTestSettings(_testSettingsDir);
        }
    }
}
