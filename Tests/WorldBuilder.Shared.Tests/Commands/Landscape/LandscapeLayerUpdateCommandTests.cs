using Moq;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Services;

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

            // Setup generic PersistDocumentAsync for LandscapeChunkDocument
            _mockDocManager.Setup(m => m.PersistDocumentAsync(It.IsAny<DocumentRental<LandscapeChunkDocument>>(), It.IsAny<ITransaction>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Unit>.Success(Unit.Value));
        }



        private (LandscapeDocument, DocumentRental<LandscapeDocument>) CreateMockTerrainRental() {
            var terrainDoc = new LandscapeDocument(_terrainDocId);

            // Bypass dats loading
            typeof(LandscapeDocument).GetField("_didLoadRegionData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(terrainDoc, true);
            typeof(LandscapeDocument).GetField("_didLoadLayers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(terrainDoc, true);

            var regionMock = new Mock<ITerrainInfo>();
            regionMock.Setup(r => r.MapWidthInVertices).Returns(1024);
            regionMock.Setup(r => r.MapHeightInVertices).Returns(1024);
            regionMock.Setup(r => r.LandblockVerticeLength).Returns(9);
            regionMock.Setup(r => r.MapWidthInLandblocks).Returns(128);
            regionMock.Setup(r => r.MapHeightInLandblocks).Returns(128);
            terrainDoc.Region = regionMock.Object;

            // Pre-load chunk for index 100
            var (chunkId, _) = terrainDoc.GetLocalVertexIndex(100u);
            var chunk = new LandscapeChunk(chunkId);
            chunk.EditsRental = new DocumentRental<LandscapeChunkDocument>(new LandscapeChunkDocument($"LandscapeChunkDocument_{chunkId}"), () => { });
            terrainDoc.LoadedChunks[chunkId] = chunk;

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
            Assert.NotNull(layer);

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
            Assert.True(terrainDoc.TryGetVertex(layer.Id, 100u, out var entry));
            Assert.Equal((byte)10, entry.Height);
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
            Assert.NotNull(layer);
            // Initial state
            terrainDoc.SetVertex(layer.Id, 100u, new TerrainEntry { Height = 5 });

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

            terrainDoc.TryGetVertex(layer.Id, 100u, out var forwardEntry);
            Assert.Equal((byte)10, forwardEntry.Height); // Verify forward

            var inverse = (LandscapeLayerUpdateCommand)command.CreateInverse();
            var backwardResult =
                await inverse.ApplyResultAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, default);

            // Assert
            Assert.True(forwardResult.IsSuccess);
            Assert.True(backwardResult.IsSuccess);
            terrainDoc.TryGetVertex(layer.Id, 100u, out var backwardEntry);
            Assert.Equal((byte)5, backwardEntry.Height); // Verify backward
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
            Assert.NotNull(layer);
            terrainDoc.SetVertex(layer.Id, 100, new TerrainEntry { Height = 10 });

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
            Assert.False(terrainDoc.TryGetVertex(layer.Id, 100u, out _));
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