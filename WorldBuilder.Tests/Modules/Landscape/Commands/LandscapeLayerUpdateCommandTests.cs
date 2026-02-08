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
    public class LandscapeLayerUpdateCommandTests
    {
        private readonly Mock<IDocumentManager> _mockDocManager;
        private readonly Mock<IDatReaderWriter> _mockDats;
        private readonly Mock<ITransaction> _mockTx;

        public LandscapeLayerUpdateCommandTests()
        {
            _mockDocManager = new Mock<IDocumentManager>();
            _mockDats = new Mock<IDatReaderWriter>();
            _mockTx = new Mock<ITransaction>();
        }

        [Fact]
        public async Task ApplyAsync_ShouldReturnSuccess()
        {
            // Arrange
            var command = new LandscapeLayerUpdateCommand();

            // Act
            var result = await command.ApplyAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.True(result.Value);
        }

        [Fact]
        public void CreateInverse_ShouldSwapChangesAndPreviousState()
        {
            // Arrange
            var changes = new Dictionary<int, TerrainEntry?> { { 1, new TerrainEntry { Height = 10 } } };
            var previous = new Dictionary<int, TerrainEntry?> { { 1, new TerrainEntry { Height = 5 } } };
            var command = new LandscapeLayerUpdateCommand
            {
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
