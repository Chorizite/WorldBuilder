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

namespace WorldBuilder.Tests.Modules.Landscape.Commands
{
    public class CreateLandscapeDocumentCommandTests
    {
        private readonly Mock<IDocumentManager> _mockDocManager;
        private readonly Mock<IDatReaderWriter> _mockDats;
        private readonly Mock<ITransaction> _mockTx;

        public CreateLandscapeDocumentCommandTests()
        {
            _mockDocManager = new Mock<IDocumentManager>();
            _mockDats = new Mock<IDatReaderWriter>();
            _mockTx = new Mock<ITransaction>();
        }

        [Fact]
        public async Task ApplyAsync_ShouldCreateDocumentAndAddBaseLayer()
        {
            // Arrange
            uint regionId = 1234;
            var terrainId = LandscapeDocument.GetIdFromRegion(regionId);
            var command = new CreateLandscapeDocumentCommand(regionId);

            var terrainDocMock = new Mock<LandscapeDocument>(terrainId);
            terrainDocMock.CallBase = true;
            terrainDocMock.Setup(m => m.InitializeForUpdatingAsync(It.IsAny<IDatReaderWriter>(), It.IsAny<IDocumentManager>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var terrainDoc = terrainDocMock.Object;
            var terrainRental = new DocumentRental<LandscapeDocument>(terrainDoc, () => { });

            _mockDocManager.Setup(m => m.CreateDocumentAsync(It.IsAny<LandscapeDocument>(), _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));

            _mockDocManager.Setup(m => m.ApplyLocalEventAsync(It.IsAny<CreateLandscapeLayerCommand>(), _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<string>.Success("base_layer_id"));

            _mockDocManager.Setup(m => m.PersistDocumentAsync(terrainRental, _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Unit>.Success(Unit.Value));

            // Act
            var result = await command.ApplyAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            _mockDocManager.Verify(m => m.CreateDocumentAsync(It.Is<LandscapeDocument>(d => d.Id == terrainId), _mockTx.Object, It.IsAny<CancellationToken>()), Times.Once);
            _mockDocManager.Verify(m => m.ApplyLocalEventAsync(It.Is<CreateLandscapeLayerCommand>(c => c.IsBase == true), _mockTx.Object, It.IsAny<CancellationToken>()), Times.Once);
            _mockDocManager.Verify(m => m.PersistDocumentAsync(terrainRental, _mockTx.Object, It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
