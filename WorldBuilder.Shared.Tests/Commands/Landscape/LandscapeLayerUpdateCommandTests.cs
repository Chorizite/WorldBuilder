using Moq;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Services;
using static WorldBuilder.Shared.Services.DocumentManager;

namespace WorldBuilder.Shared.Tests.Commands.Landscape {
    public class LandscapeLayerUpdateCommandTests {
        private readonly Mock<IDocumentManager> _mockDocManager;
        private readonly Mock<IDatReaderWriter> _mockDats;
        private readonly Mock<ITransaction> _mockTx;
        private readonly string _terrainDocId = "LandscapeDocument_1";

        public LandscapeLayerUpdateCommandTests() {
            _mockDocManager = new Mock<IDocumentManager>();
            _mockDats = new Mock<IDatReaderWriter>();
            _mockTx = new Mock<ITransaction>();
        }



        private (LandscapeDocument, DocumentRental<LandscapeDocument>) CreateMockTerrainRental() {
            var terrainDoc = new LandscapeDocument(_terrainDocId);

            // Bypass dats loading
            typeof(LandscapeDocument).GetField("_didLoadRegionData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(terrainDoc, true);
            typeof(LandscapeDocument).GetField("_didLoadLayers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(terrainDoc, true);

            var rental = new DocumentRental<LandscapeDocument>(terrainDoc, () => { });
            return (terrainDoc, rental);
        }

        [Fact]
        public async Task TerrainUpdate_WithValidChanges_AppliesSuccessfully() {
            // Arrange
            var layerId = Guid.NewGuid().ToString();
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            terrainDoc.AddLayer([], "Test Layer", false, layerId);
            var layer = terrainDoc.FindItem(layerId) as LandscapeLayer;

            var changes = new Dictionary<uint, TerrainEntry?> { { 100u, new TerrainEntry { Height = 10 } } };
            var command = new LandscapeLayerUpdateCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = layerId,
                Changes = changes
            };

            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));
            _mockDocManager.Setup(m => m.PersistDocumentAsync(It.IsAny<DocumentRental<LandscapeDocument>>(),
                    _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Unit>.Success(Unit.Value));

            // Act
            var result =
                await command.ApplyResultAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, default);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.True(layer!.Terrain.ContainsKey(100u));
            Assert.Equal((byte)10, layer.Terrain[100u].Height);
        }

        [Fact]
        public async Task TerrainUpdate_StoresPreviousState_ForUndo() {
            // Arrange
            var layerId = Guid.NewGuid().ToString();
            var changes = new Dictionary<uint, TerrainEntry?> { { 100u, new TerrainEntry { Height = 10 } } };
            var previous = new Dictionary<uint, TerrainEntry?> { { 100u, new TerrainEntry { Height = 5 } } };
            var command = new LandscapeLayerUpdateCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = layerId,
                Changes = changes,
                PreviousState = previous
            };
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            terrainDoc.AddLayer([], "Test Layer", false, layerId);

            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));
            _mockDocManager.Setup(m => m.PersistDocumentAsync(It.IsAny<DocumentRental<LandscapeDocument>>(),
                    _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Unit>.Success(Unit.Value));

            // Act
            var result =
                await command.ApplyResultAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, default);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(previous, command.PreviousState);
        }

        [Fact]
        public void CreateInverse_SwapsChangesAndPreviousState_AndPreservesIds() {
            // Arrange
            var layerId = "layer1";
            var changes = new Dictionary<uint, TerrainEntry?> { { 100u, new TerrainEntry { Height = 10 } } };
            var previous = new Dictionary<uint, TerrainEntry?> { { 100u, new TerrainEntry { Height = 5 } } };
            var command = new LandscapeLayerUpdateCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = layerId,
                Changes = changes,
                PreviousState = previous
            };

            // Act
            var inverse = (LandscapeLayerUpdateCommand)command.CreateInverse();

            // Assert
            Assert.Equal(command.PreviousState, inverse.Changes);
            Assert.Equal(command.Changes, inverse.PreviousState);
            Assert.Equal(command.TerrainDocumentId, inverse.TerrainDocumentId);
            Assert.Equal(command.LayerId, inverse.LayerId);
        }

        [Fact]
        public async Task RoundTrip_UpdateThenRevert_RestoresOriginalTerrain() {
            // Arrange
            var layerId = Guid.NewGuid().ToString();
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            terrainDoc.AddLayer([], "Test Layer", false, layerId);
            var layer = terrainDoc.FindItem(layerId) as LandscapeLayer;
            // Initial state
            layer!.Terrain[100u] = new TerrainEntry { Height = 5 };

            var changes = new Dictionary<uint, TerrainEntry?> { { 100u, new TerrainEntry { Height = 10 } } };
            var previous = new Dictionary<uint, TerrainEntry?> { { 100u, new TerrainEntry { Height = 5 } } };
            var command = new LandscapeLayerUpdateCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = layerId,
                Changes = changes,
                PreviousState = previous
            };

            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));
            _mockDocManager.Setup(m => m.PersistDocumentAsync(It.IsAny<DocumentRental<LandscapeDocument>>(),
                    _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Unit>.Success(Unit.Value));

            // Act
            var forwardResult =
                await command.ApplyResultAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, default);

            Assert.Equal((byte)10, layer.Terrain[100u].Height); // Verify forward

            var inverse = (LandscapeLayerUpdateCommand)command.CreateInverse();
            var backwardResult =
                await inverse.ApplyResultAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, default);

            // Assert
            Assert.True(forwardResult.IsSuccess);
            Assert.True(backwardResult.IsSuccess);
            Assert.Equal((byte)5, layer.Terrain[100u].Height); // Verify backward
        }

        [Fact]
        public async Task TerrainUpdate_WithEmptyChanges_SucceedsNoOp() {
            // Arrange
            var layerId = Guid.NewGuid().ToString();
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            terrainDoc.AddLayer([], "Test Layer", false, layerId);

            var command = new LandscapeLayerUpdateCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = layerId,
                Changes = new Dictionary<uint, TerrainEntry?>()
            };

            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));
            _mockDocManager.Setup(m => m.PersistDocumentAsync(It.IsAny<DocumentRental<LandscapeDocument>>(),
                    _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Unit>.Success(Unit.Value));

            // Act
            var result =
                await command.ApplyResultAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, default);

            // Assert
            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task TerrainUpdate_WithNullValues_RemovesTerrainData() {
            // Arrange
            var layerId = Guid.NewGuid().ToString();
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            terrainDoc.AddLayer([], "Test Layer", false, layerId);
            var layer = terrainDoc.FindItem(layerId) as LandscapeLayer;
            layer!.Terrain[100] = new TerrainEntry { Height = 10 };

            var changes = new Dictionary<uint, TerrainEntry?> { { 100u, null } };
            var command = new LandscapeLayerUpdateCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = layerId,
                Changes = changes
            };

            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));
            _mockDocManager.Setup(m => m.PersistDocumentAsync(It.IsAny<DocumentRental<LandscapeDocument>>(),
                    _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Unit>.Success(Unit.Value));

            // Act
            var result =
                await command.ApplyResultAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, default);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.False(layer.Terrain.ContainsKey(100u));
        }

        [Fact]
        public async Task TerrainUpdate_WhenLayerNotFound_ReturnsFailure() {
            // Arrange
            var layerId = Guid.NewGuid().ToString();
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            // Do NOT add layer

            var changes = new Dictionary<uint, TerrainEntry?> { { 100u, new TerrainEntry { Height = 10 } } };
            var command = new LandscapeLayerUpdateCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = layerId,
                Changes = changes
            };

            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));

            // Act
            var result =
                await command.ApplyResultAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, default);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Contains("Layer not found", result.Error.Message);
        }
    }
}
