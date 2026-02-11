using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Services;
using WorldBuilder.Shared.Lib;
using DatReaderWriter.Lib;
using System.Linq;

namespace WorldBuilder.Tests.Modules.Landscape.Commands
{
    public class ReorderLandscapeLayerCommandTests
    {
        private readonly Mock<IDocumentManager> _mockDocManager;
        private readonly Mock<IDatReaderWriter> _mockDats;
        private readonly Mock<ITransaction> _mockTx;

        public ReorderLandscapeLayerCommandTests()
        {
            _mockDocManager = new Mock<IDocumentManager>();
            _mockDats = new Mock<IDatReaderWriter>();
            _mockTx = new Mock<ITransaction>();
        }

        [Fact]
        public async Task ApplyAsync_ShouldReorderLayerInTerrain()
        {
            // Arrange
            var terrainId = "LandscapeDocument_1";
            var layer1Id = "Layer_1";
            var layer2Id = "Layer_2";
            var command = new ReorderLandscapeLayerCommand(terrainId, new List<string>(), layer2Id, 0, 1);

            var terrainDocMock = new Mock<LandscapeDocument>(terrainId);
            terrainDocMock.CallBase = true;
            terrainDocMock.Setup(m => m.InitializeForUpdatingAsync(It.IsAny<IDatReaderWriter>(), It.IsAny<IDocumentManager>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var terrainDoc = terrainDocMock.Object;
            terrainDoc.AddLayer(new List<string>(), "Layer 1", false, layer1Id);
            terrainDoc.AddLayer(new List<string>(), "Layer 2", false, layer2Id);

            // Initial order: [Layer 1, Layer 2]

            var terrainRental = new DocumentRental<LandscapeDocument>(terrainDoc, () => { });

            _mockDocManager.Setup(m => m.RentDocumentAsync<LandscapeDocument>(terrainId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));

            _mockDocManager.Setup(m => m.PersistDocumentAsync(terrainRental, _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Unit>.Success(Unit.Value));

            // Act
            var result = await command.ApplyAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            var layers = terrainDoc.LayerTree;
            Assert.Equal(layer2Id, layers[0].Id);
            Assert.Equal(layer1Id, layers[1].Id);
            _mockDocManager.Verify(m => m.PersistDocumentAsync(terrainRental, _mockTx.Object, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void CreateInverse_ShouldReturnReorderCommandWithSwappedIndices()
        {
            // Arrange
            var command = new ReorderLandscapeLayerCommand("terrain1", new List<string>(), "layer1", 5, 2)
            {
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
