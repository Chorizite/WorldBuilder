using DatReaderWriter.Lib;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Services;
using Xunit;

namespace WorldBuilder.Tests.Modules.Landscape.Commands {
    public class ReorderLandscapeLayerCommandTests {
        private readonly Mock<IDocumentManager> _mockDocManager;
        private readonly Mock<IDatReaderWriter> _mockDats;
        private readonly Mock<ITransaction> _mockTx;

        public ReorderLandscapeLayerCommandTests() {
            _mockDocManager = new Mock<IDocumentManager>();
            _mockDats = new Mock<IDatReaderWriter>();
            _mockTx = new Mock<ITransaction>();
        }

        [Fact]
        public async Task ApplyAsync_ShouldReorderLayerInTerrain() {
            // Arrange
            var terrainId = "LandscapeDocument_1";
            var layer2Id = "Layer_2";
            var command = new ReorderLandscapeLayerCommand(terrainId, new List<string>(), layer2Id, 0, 1);

            var terrainDocMock = new Mock<LandscapeDocument>(terrainId);
            var layers = new List<LandscapeLayerBase> {
                new LandscapeLayer("Layer 1") { Id = "Layer_1" },
                new LandscapeLayer("Layer 2") { Id = "Layer_2" }
            };
            
            terrainDocMock.Setup(m => m.LayerTree).Returns(layers);
            terrainDocMock.Setup(m => m.FindItem(It.IsAny<string>()))
                .Returns<string>(id => layers.FirstOrDefault(l => l.Id == id));
            
            terrainDocMock.Setup(m => m.GetAffectedVertices(It.IsAny<LandscapeLayerBase>()))
                .Returns([]);
            terrainDocMock.Setup(m => m.InitializeForUpdatingAsync(It.IsAny<IDatReaderWriter>(), It.IsAny<IDocumentManager>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            terrainDocMock.Setup(m => m.SyncLayerTreeAsync(It.IsAny<ITransaction>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            terrainDocMock.Setup(m => m.RecalculateTerrainCacheAsync(It.IsAny<IEnumerable<uint>>()))
                .Returns(Task.CompletedTask);

            var terrainDoc = terrainDocMock.Object;
            // No need to manually add layers as we are just verifying the call to SyncLayerTreeAsync

            var terrainRental = new DocumentRental<LandscapeDocument>(terrainDoc, () => { });

            _mockDocManager.Setup(m => m.RentDocumentAsync<LandscapeDocument>(terrainId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));

            // Act
            var result = await command.ApplyAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            terrainDocMock.Verify(m => m.SyncLayerTreeAsync(It.IsAny<ITransaction>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void CreateInverse_ShouldReturnReorderCommandWithSwappedIndices() {
            // Arrange
            var command = new ReorderLandscapeLayerCommand("terrain1", new List<string>(), "layer1", 5, 2) {
                UserId = "user1"
            };

            // Act
            var inverse = command.CreateInverse();

            // Assert
            var reorderInverse = Assert.IsType<ReorderLandscapeLayerCommand>(inverse);
            Assert.Equal(2, reorderInverse.NewIndex);
            Assert.Equal(5, reorderInverse.OldIndex);
            Assert.Equal("user1", reorderInverse.UserId);
        }
    }
}