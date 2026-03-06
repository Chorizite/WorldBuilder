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
            _mockDocManager.Setup(m => m.LandscapeDataProvider).Returns(new Mock<WorldBuilder.Shared.Modules.Landscape.Services.ILandscapeDataProvider>().Object);
            _mockDocManager.Setup(m => m.LandscapeCacheService).Returns(new Mock<WorldBuilder.Shared.Modules.Landscape.Services.ILandscapeCacheService>().Object);
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
            terrainDocMock.Setup(m => m.InitializeForUpdatingAsync(It.IsAny<IDatReaderWriter>(), It.IsAny<IDocumentManager>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            terrainDocMock.Setup(m => m.SyncLayerTreeAsync(It.IsAny<ITransaction?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Mock AddLayer since it's not virtual, we ensure the mock is configured
            // but for non-virtual methods, Moq can't intercept them unless we make them virtual.
            // Let's make AddLayer, RemoveLayer virtual as well.

            var terrainDoc = terrainDocMock.Object;

            // Inject dependencies manually
            typeof(LandscapeDocument).GetField("_documentManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(terrainDoc, _mockDocManager.Object);

            var terrainRental = new DocumentRental<LandscapeDocument>(terrainDoc, () => { });

            _mockDocManager.Setup(m => m.RentDocumentAsync<LandscapeDocument>(terrainId, It.IsAny<ITransaction?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));

            // Act
            var result = await command.ApplyAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess, result.Error?.Message);
            terrainDocMock.Verify(m => m.SyncLayerTreeAsync(It.IsAny<ITransaction?>(), It.IsAny<CancellationToken>()), Times.Once);
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