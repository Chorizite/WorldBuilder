using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Services;
using Xunit;

namespace WorldBuilder.Shared.Tests.Services {
    public class DatRepositoryServiceTests : IDisposable {
        private readonly string _testRoot;
        private readonly DatRepositoryService _service;

        public DatRepositoryServiceTests() {
            _testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testRoot);
            _service = new DatRepositoryService(new NullLogger<DatRepositoryService>());
            _service.SetRepositoryRoot(_testRoot);
        }

        public void Dispose() {
            if (Directory.Exists(_testRoot)) {
                Directory.Delete(_testRoot, true);
            }
        }

        [Fact]
        public async Task UpdateFriendlyNameAsync_UpdatesNameAndPersists() {
            // Arrange
            // Create a fake registry file manually to avoid needing real DATs for import
            var registryPath = Path.Combine(_testRoot, "managed_dats.json");
            var id = Guid.NewGuid();
            var json = @$"[{{""Id"":""{id}"",""FriendlyName"":""Old Name""}}]";
            File.WriteAllText(registryPath, json);
            _service.SetRepositoryRoot(_testRoot); // Reload

            // Act
            var result = await _service.UpdateFriendlyNameAsync(id, "New Name", CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            var set = _service.GetManagedDataSet(id);
            Assert.Equal("New Name", set?.FriendlyName);

            // Verify persistence by reloading service
            var newService = new DatRepositoryService(new NullLogger<DatRepositoryService>());
            newService.SetRepositoryRoot(_testRoot);
            var reloadedSet = newService.GetManagedDataSet(id);
            Assert.Equal("New Name", reloadedSet?.FriendlyName);
        }

        [Fact]
        public void LoadRegistry_AppliesHardcodedDefaultNames() {
            // Arrange
            var registryPath = Path.Combine(_testRoot, "managed_dats.json");
            var id = Guid.NewGuid();
            var combinedMd5 = "C328EAFE1234567890ABCDEF12345678";
            var portalIter = 2072;
            var cellIter = 982;
            var generatedName = $"Iteration P:{portalIter} C:{cellIter} ({combinedMd5[..8]})";
            
            // Save with the default generated name
            var json = @$"[{{""Id"":""{id}"",""FriendlyName"":""{generatedName}"",""PortalIteration"":{portalIter},""CellIteration"":{cellIter},""CombinedMd5"":""{combinedMd5}""}}]";
            File.WriteAllText(registryPath, json);

            // Act
            _service.SetRepositoryRoot(_testRoot); // Reload

            // Assert
            var set = _service.GetManagedDataSet(id);
            Assert.Equal("EndOfRetail", set?.FriendlyName);
        }

        [Fact]
        public async Task UpdateFriendlyNameAsync_ReturnsFailureIfNotFound() {
            // Act
            var result = await _service.UpdateFriendlyNameAsync(Guid.NewGuid(), "New Name", CancellationToken.None);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Equal("NOT_FOUND", result.Error.Code);
        }
    }
}
