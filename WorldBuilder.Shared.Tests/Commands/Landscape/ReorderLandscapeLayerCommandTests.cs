using Moq;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Services;
using WorldBuilder.Shared.Tests.Mocks;

namespace WorldBuilder.Shared.Tests.Commands.Landscape {
    public class ReorderLandscapeLayerCommandTests {
        private readonly Mock<IDocumentManager> _mockDocManager;
        private readonly MockDatReaderWriter _dats;
        private readonly Mock<ITransaction> _mockTx;
        private readonly string _terrainDocId = "LandscapeDocument_1";
        private const string L1 = "LandscapeLayerDocument_1";
        private const string L2 = "LandscapeLayerDocument_2";
        private const string L3 = "LandscapeLayerDocument_3";

        public ReorderLandscapeLayerCommandTests() {
            _mockDocManager = new Mock<IDocumentManager>();
            _dats = new MockDatReaderWriter();
            _mockTx = new Mock<ITransaction>();
        }

        private (LandscapeDocument, DocumentRental<LandscapeDocument>) CreateMockTerrainRental() {
            var terrainDoc = new LandscapeDocument(_terrainDocId);
            var rental = new DocumentRental<LandscapeDocument>(terrainDoc, () => { });
            return (terrainDoc, rental);
        }



        [Fact]
        public async Task ReorderLayer_WithValidIndices_ReordersSuccessfully() {
            // Arrange
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();

            // Initial Tree: [L1, L2, L3]
            terrainDoc.AddLayer([], "Base", true, L1);
            terrainDoc.AddLayer([], "Layer 2", false, L2);
            terrainDoc.AddLayer([], "Layer 3", false, L3);

            // Move L3 (index 2) to 1 -> [L1, L3, L2]
            var command = new ReorderLandscapeLayerCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = L3,
                GroupPath = [],
                OldIndex = 2,
                NewIndex = 1
            };


            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));
            _mockDocManager.Setup(m => m.PersistDocumentAsync(It.IsAny<DocumentRental<LandscapeDocument>>(),
                    _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Unit>.Success(Unit.Value));

            // Act
            var result =
                await command.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);

            // Assert
            Assert.True(result.IsSuccess);
            var layers = terrainDoc.GetAllLayers().ToList(); // [L1, L3, L2]
            Assert.Equal(L1, layers[0].Id);
            Assert.Equal(L3, layers[1].Id);
            Assert.Equal(L2, layers[2].Id);
        }

        [Fact]
        public async Task ReorderLayer_MoveUp_UpdatesOrder() {
            // Arrange
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();

            // Initial Tree: [L1, L2, L3]
            terrainDoc.AddLayer([], "Base", true, L1);
            terrainDoc.AddLayer([], "Layer 2", false, L2);
            terrainDoc.AddLayer([], "Layer 3", false, L3);

            // Move L3 (index 2) to 1 -> [L1, L3, L2]
            var command = new ReorderLandscapeLayerCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = L3,
                GroupPath = [],
                OldIndex = 2,
                NewIndex = 1
            };


            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));
            _mockDocManager.Setup(m => m.PersistDocumentAsync(It.IsAny<DocumentRental<LandscapeDocument>>(),
                    _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Unit>.Success(Unit.Value));

            // Act
            await command.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);

            // Assert
            var layers = terrainDoc.GetAllLayers().ToList();
            Assert.Equal(L3, layers[1].Id);
        }

        [Fact]
        public async Task ReorderLayer_MoveDown_UpdatesOrder() {
            // Arrange
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();

            // Initial Tree: [L1, L2, L3]
            terrainDoc.AddLayer([], "Base", true, L1);
            terrainDoc.AddLayer([], "Layer 2", false, L2);
            terrainDoc.AddLayer([], "Layer 3", false, L3);

            // Move L2 (index 1) to 2 -> [L1, L3, L2]
            var command = new ReorderLandscapeLayerCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = L2,
                GroupPath = [],
                OldIndex = 1,
                NewIndex = 2
            };


            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));
            _mockDocManager.Setup(m => m.PersistDocumentAsync(It.IsAny<DocumentRental<LandscapeDocument>>(),
                    _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Unit>.Success(Unit.Value));

            // Act
            await command.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);

            // Assert
            var layers = terrainDoc.GetAllLayers().ToList(); // [L1, L3, L2]
            Assert.Equal(L1, layers[0].Id);
            Assert.Equal(L3, layers[1].Id);
            Assert.Equal(L2, layers[2].Id);
        }

        [Fact]
        public async Task ReorderLayer_UpdatesParentDocumentVersion() {
            // Arrange
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            terrainDoc.AddLayer([], "Base", true, L1);
            terrainDoc.AddLayer([], "Layer 2", false, L2);

            var initialVersion = terrainDoc.Version;

            var command = new ReorderLandscapeLayerCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = L2,
                GroupPath = [],
                OldIndex = 1,
                NewIndex = 1
            };


            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));
            _mockDocManager.Setup(m => m.PersistDocumentAsync(It.IsAny<DocumentRental<LandscapeDocument>>(),
                    _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Unit>.Success(Unit.Value));

            // Act
            await command.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);

            // Assert
            Assert.Equal(initialVersion + 1, terrainDoc.Version);
        }

        [Fact]
        public async Task ReorderLayer_BaseLayerFromIndex0_ThrowsException() {
            // Arrange
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            terrainDoc.AddLayer([], "Base", true, L1);
            terrainDoc.AddLayer([], "Layer 2", false, L2);

            var command = new ReorderLandscapeLayerCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = L1,
                GroupPath = [],
                OldIndex = 0,
                NewIndex = 1
            };


            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));

            // Act & Assert
            var result =
                await command.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);
            Assert.True(result.IsFailure);
            Assert.Contains("Cannot reorder the base layer from position 0", result.Error.Message);
        }

        [Fact]
        public async Task ReorderLayer_NonBaseLayer_AllowsReordering() {
            // Arrange
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            terrainDoc.AddLayer([], "Base", true, L1);
            terrainDoc.AddLayer([], "Layer 2", false, L2);

            var command = new ReorderLandscapeLayerCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = L2,
                GroupPath = [],
                OldIndex = 1,
                NewIndex = 1
            };


            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));
            _mockDocManager.Setup(m => m.PersistDocumentAsync(It.IsAny<DocumentRental<LandscapeDocument>>(),
                    _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Unit>.Success(Unit.Value));

            // Act
            var result =
                await command.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);

            // Assert
            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task ReorderLayer_WithNegativeIndex_ThrowsException() {
            // Arrange
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            terrainDoc.AddLayer([], "Base", true, L1);
            terrainDoc.AddLayer([], "Layer 2", false, L2);

            var command = new ReorderLandscapeLayerCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = L2,
                GroupPath = [],
                OldIndex = 1,
                NewIndex = -1
            };


            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));

            // Act & Assert
            var result =
                await command.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);
            Assert.True(result.IsFailure);
        }

        [Fact]
        public async Task ReorderLayer_WithIndexTooLarge_ThrowsException() {
            // Arrange
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            terrainDoc.AddLayer([], "Base", true, L1);
            terrainDoc.AddLayer([], "Layer 2", false, L2);

            var command = new ReorderLandscapeLayerCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = L2,
                GroupPath = [],
                OldIndex = 1,
                NewIndex = 10
            };


            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));

            // Act & Assert
            var result =
                await command.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);
            Assert.True(result.IsFailure);
        }

        [Fact]
        public async Task ReorderLayer_SameIndex_SucceedsNoOp() {
            // Arrange
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            terrainDoc.AddLayer([], "Base", true, L1);
            terrainDoc.AddLayer([], "Layer 2", false, L2);

            var command = new ReorderLandscapeLayerCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = L2,
                GroupPath = [],
                OldIndex = 1,
                NewIndex = 1
            };


            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));
            _mockDocManager.Setup(m => m.PersistDocumentAsync(It.IsAny<DocumentRental<LandscapeDocument>>(),
                    _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Unit>.Success(Unit.Value));

            // Act
            var result =
                await command.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);

            // Assert
            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task ReorderLayer_WhenTerrainDocumentNotFound_ReturnsFailure() {
            // Arrange
            var command = new ReorderLandscapeLayerCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = "someid",
                GroupPath = [],
                OldIndex = 1,
                NewIndex = 0
            };

            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Failure("Not Found"));

            // Act
            var result =
                await command.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Equal("Not Found", result.Error.Message);
        }

        [Fact]
        public async Task ReorderLayer_WhenLayerNotFound_ThrowsException() {
            // Arrange
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            terrainDoc.AddLayer([], "Base", true, L1);

            var command = new ReorderLandscapeLayerCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = "nonexistent",
                GroupPath = [],
                OldIndex = 1,
                NewIndex = 0
            };


            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));

            // Act
            var result =
                await command.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);

            // Assert
            Assert.True(result.IsFailure);
        }

        [Fact]
        public void CreateInverse_SwapsOldAndNewIndices() {
            // Arrange
            var command = new ReorderLandscapeLayerCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = "layerId",
                GroupPath = ["group"],
                OldIndex = 1,
                NewIndex = 2
            };

            // Act
            var inverse = (ReorderLandscapeLayerCommand)command.CreateInverse();

            // Assert
            Assert.Equal(command.OldIndex, inverse.NewIndex);
            Assert.Equal(command.NewIndex, inverse.OldIndex);
            Assert.Equal(command.LayerId, inverse.LayerId);
            Assert.Equal(command.GroupPath, inverse.GroupPath);
        }

        [Fact]
        public async Task RoundTrip_ReorderThenRevert_RestoresOriginalOrder() {
            // Arrange
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();

            // Initial Tree: [L1, L2, L3]
            terrainDoc.AddLayer([], "Base", true, L1);
            terrainDoc.AddLayer([], "Layer 2", false, L2);
            terrainDoc.AddLayer([], "Layer 3", false, L3);

            var command = new ReorderLandscapeLayerCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = L3,
                GroupPath = [],
                OldIndex = 2,
                NewIndex = 1
            };


            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));
            _mockDocManager.Setup(m => m.PersistDocumentAsync(It.IsAny<DocumentRental<LandscapeDocument>>(),
                    _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Unit>.Success(Unit.Value));

            // Act - Forward -> [L1, L3, L2]
            await command.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);

            // Act - Backward -> [L1, L2, L3]
            var inverse = (ReorderLandscapeLayerCommand)command.CreateInverse();
            await inverse.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);

            // Assert
            var layers = terrainDoc.GetAllLayers().ToList(); // [L1, L2, L3]
            Assert.Equal(L1, layers[0].Id);
            Assert.Equal(L2, layers[1].Id);
            Assert.Equal(L3, layers[2].Id);
        }
    }
}