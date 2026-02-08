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
    public class CreateLandscapeLayerGroupCommandTests
    {
        private readonly Mock<IDocumentManager> _mockDocManager;
        private readonly Mock<IDatReaderWriter> _mockDats;
        private readonly Mock<ITransaction> _mockTx;

        public CreateLandscapeLayerGroupCommandTests()
        {
            _mockDocManager = new Mock<IDocumentManager>();
            _mockDats = new Mock<IDatReaderWriter>();
            _mockTx = new Mock<ITransaction>();
        }

        [Fact]
        public async Task ApplyAsync_ShouldAddGroupToTerrain()
        {
            // Arrange
            var terrainId = "LandscapeDocument_1";
            var groupId = "Group_1";
            var command = new CreateLandscapeLayerGroupCommand(terrainId, new List<string>(), "New Group")
            {
                GroupId = groupId
            };

            var terrainDocMock = new Mock<LandscapeDocument>(terrainId);
            terrainDocMock.CallBase = true;
            terrainDocMock.Setup(m => m.InitializeForUpdatingAsync(It.IsAny<IDatReaderWriter>(), It.IsAny<IDocumentManager>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var terrainDoc = terrainDocMock.Object;
            var terrainRental = new DocumentManager.DocumentRental<LandscapeDocument>(terrainDoc, () => { });

            _mockDocManager.Setup(m => m.RentDocumentAsync<LandscapeDocument>(terrainId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentManager.DocumentRental<LandscapeDocument>>.Success(terrainRental));

            _mockDocManager.Setup(m => m.PersistDocumentAsync(terrainRental, _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Unit>.Success(Unit.Value));

            // Act
            var result = await command.ApplyAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Contains(terrainDoc.LayerTree, l => l is LandscapeLayerGroup g && g.Id == groupId && g.Name == "New Group");
            _mockDocManager.Verify(m => m.PersistDocumentAsync(terrainRental, _mockTx.Object, It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
