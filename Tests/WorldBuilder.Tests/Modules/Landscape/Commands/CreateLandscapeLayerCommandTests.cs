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
    public class CreateLandscapeLayerCommandTests {
        private readonly Mock<IDocumentManager> _mockDocManager;
        private readonly Mock<IDatReaderWriter> _mockDats;
        private readonly Mock<ITransaction> _mockTx;

        public CreateLandscapeLayerCommandTests() {
            _mockDocManager = new Mock<IDocumentManager>();
            _mockDats = new Mock<IDatReaderWriter>();
            _mockTx = new Mock<ITransaction>();
        }

        [Fact]
        public async Task ApplyAsync_ShouldCreateLayerAndAddToTerrain() {
            // Arrange
            var terrainId = "LandscapeDocument_1";
            var layerId = "LandscapeLayerDocument_1";
            var command = new CreateLandscapeLayerCommand(terrainId, new List<string>(), "New Layer", false) {
                LayerId = layerId
            };

            var terrainDocMock = new Mock<LandscapeDocument>(terrainId);
            terrainDocMock.CallBase = true;
            terrainDocMock.Setup(m => m.InitializeForUpdatingAsync(It.IsAny<IDatReaderWriter>(), It.IsAny<IDocumentManager>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var terrainDoc = terrainDocMock.Object;

            var terrainRental = new DocumentRental<LandscapeDocument>(terrainDoc, () => { });

            _mockDocManager.Setup(m => m.RentDocumentAsync<LandscapeDocument>(terrainId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));

            _mockDocManager.Setup(m => m.PersistDocumentAsync(terrainRental, _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Unit>.Success(Unit.Value));

            // Act
            var result = await command.ApplyAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Contains(terrainDoc.GetAllLayers(), l => l.Id == layerId && l.Name == "New Layer");
            _mockDocManager.Verify(m => m.PersistDocumentAsync(terrainRental, _mockTx.Object, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void CreateInverse_ShouldReturnDeleteCommand() {
            // Arrange
            var terrainId = "LandscapeDocument_1";
            var layerId = "Layer_1";
            var command = new CreateLandscapeLayerCommand(terrainId, new List<string> { "Group1" }, "New Layer", false) {
                LayerId = layerId,
                UserId = "user1"
            };

            // Act
            var inverse = command.CreateInverse();

            // Assert
            var deleteCommand = Assert.IsType<DeleteLandscapeLayerCommand>(inverse);
            Assert.Equal(terrainId, deleteCommand.TerrainDocumentId);
            Assert.Equal(layerId, deleteCommand.LayerId);
            Assert.Equal("New Layer", deleteCommand.Name);
            Assert.Equal("user1", deleteCommand.UserId);
            Assert.Equal(command.GroupPath, deleteCommand.GroupPath);
        }
    }
}