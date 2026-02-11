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
    public class DeleteLandscapeLayerCommandTests {
        private readonly Mock<IDocumentManager> _mockDocManager;
        private readonly Mock<IDatReaderWriter> _mockDats;
        private readonly Mock<ITransaction> _mockTx;

        public DeleteLandscapeLayerCommandTests() {
            _mockDocManager = new Mock<IDocumentManager>();
            _mockDats = new Mock<IDatReaderWriter>();
            _mockTx = new Mock<ITransaction>();
        }

        [Fact]
        public async Task ApplyAsync_ShouldRemoveLayerFromTerrain() {
            // Arrange
            var terrainId = "LandscapeDocument_1";
            var layerId = "Layer_1";
            var command = new DeleteLandscapeLayerCommand {
                TerrainDocumentId = terrainId,
                LayerId = layerId,
                GroupPath = new List<string>()
            };

            var terrainDocMock = new Mock<LandscapeDocument>(terrainId);
            terrainDocMock.CallBase = true; // Use real implementation for non-virtual methods like AddLayer
            terrainDocMock.Setup(m => m.InitializeForUpdatingAsync(It.IsAny<IDatReaderWriter>(), It.IsAny<IDocumentManager>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var terrainDoc = terrainDocMock.Object;
            terrainDoc.AddLayer(new List<string>(), "Layer 1", false, layerId);

            var terrainRental = new DocumentRental<LandscapeDocument>(terrainDoc, () => { });

            _mockDocManager.Setup(m => m.RentDocumentAsync<LandscapeDocument>(terrainId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));

            _mockDocManager.Setup(m => m.PersistDocumentAsync(terrainRental, _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Unit>.Success(Unit.Value));

            // Act
            var result = await command.ApplyAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.DoesNotContain(terrainDoc.GetAllLayers(), l => l.Id == layerId);
            _mockDocManager.Verify(m => m.PersistDocumentAsync(terrainRental, _mockTx.Object, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void CreateInverse_ShouldReturnCreateCommand() {
            // Arrange
            var terrainId = "LandscapeDocument_1";
            var layerId = "Layer_1";
            var command = new DeleteLandscapeLayerCommand {
                TerrainDocumentId = terrainId,
                LayerId = layerId,
                GroupPath = new List<string> { "Group1" },
                Name = "Layer 1",
                IsBase = false,
                UserId = "user1"
            };

            // Act
            var inverse = command.CreateInverse();

            // Assert
            var createCommand = Assert.IsType<CreateLandscapeLayerCommand>(inverse);
            Assert.Equal(terrainId, createCommand.TerrainDocumentId);
            Assert.Equal(layerId, createCommand.LayerId);
            Assert.Equal("Layer 1", createCommand.Name);
            Assert.Equal("user1", createCommand.UserId);
            Assert.Equal(command.GroupPath, createCommand.GroupPath);
        }
    }
}