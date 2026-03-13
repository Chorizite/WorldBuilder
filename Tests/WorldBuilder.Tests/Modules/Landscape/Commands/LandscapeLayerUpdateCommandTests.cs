using DatReaderWriter.Lib;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Services;
using Xunit;

namespace WorldBuilder.Tests.Modules.Landscape.Commands {
    public class LandscapeLayerUpdateCommandTests {
        private readonly Mock<IDocumentManager> _mockDocManager;
        private readonly Mock<IDatReaderWriter> _mockDats;
        private readonly Mock<ITransaction> _mockTx;

        public LandscapeLayerUpdateCommandTests() {
            _mockDocManager = new Mock<IDocumentManager>();
            _mockDocManager.Setup(m => m.LandscapeDataProvider).Returns(new Mock<WorldBuilder.Shared.Modules.Landscape.Services.ILandscapeDataProvider>().Object);
            _mockDocManager.Setup(m => m.LandscapeCacheService).Returns(new Mock<WorldBuilder.Shared.Modules.Landscape.Services.ILandscapeCacheService>().Object);
            _mockDats = new Mock<IDatReaderWriter>();
            _mockTx = new Mock<ITransaction>();
        }

        [Fact]
        public async Task ApplyAsync_ShouldReturnSuccess() {
            // Arrange
            var terrainId = "LandscapeDocument_1";
            var layerId = "Layer_1";
            var command = new LandscapeLayerUpdateCommand {
                TerrainDocumentId = terrainId,
                LayerId = layerId,
                Changes = new Dictionary<uint, TerrainEntry?>()
            };

            var terrainDocMock = new Mock<LandscapeDocument>(terrainId);
            terrainDocMock.CallBase = true;
            terrainDocMock.Setup(m => m.InitializeForUpdatingAsync(It.IsAny<IDatReaderWriter>(), It.IsAny<IDocumentManager>(), It.IsAny<ITransaction>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var terrainDoc = terrainDocMock.Object;
            terrainDoc.AddLayer(new List<string>(), "Layer 1", false, layerId);

            var terrainRental = new DocumentRental<LandscapeDocument>(terrainDoc, () => { });

            // Inject dependencies manually
            typeof(LandscapeDocument).GetField("_documentManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(terrainDoc, _mockDocManager.Object);

            _mockDocManager.Setup(m => m.RentDocumentAsync<LandscapeDocument>(terrainId, It.IsAny<ITransaction?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));

            _mockDocManager.Setup(m => m.PersistDocumentAsync(terrainRental, It.IsAny<ITransaction?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Unit>.Success(Unit.Value));

            // Act
            var result = await command.ApplyAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.True(result.Value);
        }

        [Fact]
        public void CreateInverse_ShouldSwapChangesAndPreviousState() {
            // Arrange
            var changes = new Dictionary<uint, TerrainEntry?> { { 1u, new TerrainEntry { Height = 10 } } };
            var previous = new Dictionary<uint, TerrainEntry?> { { 1u, new TerrainEntry { Height = 5 } } };
            var command = new LandscapeLayerUpdateCommand {
                Changes = changes,
                PreviousState = previous,
                UserId = "user1"
            };

            // Act
            var inverse = command.CreateInverse();

            // Assert
            var updateInverse = Assert.IsType<LandscapeLayerUpdateCommand>(inverse);
            Assert.Equal(previous, updateInverse.Changes);
            Assert.Equal(changes, updateInverse.PreviousState);
            Assert.Equal("user1", updateInverse.UserId);
        }
    }
}