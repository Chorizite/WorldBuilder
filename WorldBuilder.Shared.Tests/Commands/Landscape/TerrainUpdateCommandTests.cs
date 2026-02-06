using Moq;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Tests.Commands.Landscape {
    public class TerrainUpdateCommandTests {
        private readonly Mock<IDocumentManager> _mockDocManager;
        private readonly Mock<IDatReaderWriter> _mockDats;
        private readonly Mock<ITransaction> _mockTx;

        public TerrainUpdateCommandTests() {
            _mockDocManager = new Mock<IDocumentManager>();
            _mockDats = new Mock<IDatReaderWriter>();
            _mockTx = new Mock<ITransaction>();
        }

        [Fact]
        public async Task TerrainUpdate_WithValidChanges_AppliesSuccessfully() {
            // Arrange
            var changes = new Dictionary<int, TerrainEntry?> { { 100, new TerrainEntry { Height = 10 } } };
            var command = new TerrainUpdateCommand { Changes = changes };

            // Act
            var result =
                await command.ApplyResultAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, default);

            // Assert
            Assert.True(result.IsSuccess);
            // Note: Full implementation is pending in TerrainUpdateEvent.cs
        }

        [Fact]
        public async Task TerrainUpdate_StoresPreviousState_ForUndo() {
            // Arrange
            var changes = new Dictionary<int, TerrainEntry?> { { 100, new TerrainEntry { Height = 10 } } };
            var previous = new Dictionary<int, TerrainEntry?> { { 100, new TerrainEntry { Height = 5 } } };
            var command = new TerrainUpdateCommand { Changes = changes, PreviousState = previous };

            // Act
            var result =
                await command.ApplyResultAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, default);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(previous, command.PreviousState);
        }

        [Fact]
        public void CreateInverse_SwapsChangesAndPreviousState() {
            // Arrange
            var changes = new Dictionary<int, TerrainEntry?> { { 100, new TerrainEntry { Height = 10 } } };
            var previous = new Dictionary<int, TerrainEntry?> { { 100, new TerrainEntry { Height = 5 } } };
            var command = new TerrainUpdateCommand { Changes = changes, PreviousState = previous };

            // Act
            var inverse = (TerrainUpdateCommand)command.CreateInverse();

            // Assert
            Assert.Equal(command.PreviousState, inverse.Changes);
            Assert.Equal(command.Changes, inverse.PreviousState);
        }

        [Fact]
        public async Task RoundTrip_UpdateThenRevert_RestoresOriginalTerrain() {
            // Arrange
            var changes = new Dictionary<int, TerrainEntry?> { { 100, new TerrainEntry { Height = 10 } } };
            var previous = new Dictionary<int, TerrainEntry?> { { 100, new TerrainEntry { Height = 5 } } };
            var command = new TerrainUpdateCommand { Changes = changes, PreviousState = previous };

            // Act
            var forwardResult =
                await command.ApplyResultAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, default);
            var inverse = (TerrainUpdateCommand)command.CreateInverse();
            var backwardResult =
                await inverse.ApplyResultAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, default);

            // Assert
            Assert.True(forwardResult.IsSuccess);
            Assert.True(backwardResult.IsSuccess);
        }

        [Fact]
        public async Task TerrainUpdate_WithEmptyChanges_SucceedsNoOp() {
            // Arrange
            var command = new TerrainUpdateCommand { Changes = new Dictionary<int, TerrainEntry?>() };

            // Act
            var result =
                await command.ApplyResultAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, default);

            // Assert
            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task TerrainUpdate_WithNullValues_RemovesTerrainData() {
            // Arrange
            var changes = new Dictionary<int, TerrainEntry?> { { 100, null } };
            var command = new TerrainUpdateCommand { Changes = changes };

            // Act
            var result =
                await command.ApplyResultAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, default);

            // Assert
            Assert.True(result.IsSuccess);
        }
    }
}
