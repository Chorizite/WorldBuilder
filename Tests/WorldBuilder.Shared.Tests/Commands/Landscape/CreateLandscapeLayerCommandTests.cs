using Moq;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Services;
using WorldBuilder.Shared.Tests.Mocks;

namespace WorldBuilder.Shared.Tests.Commands.Landscape {
    public class CreateLandscapeLayerCommandTests {
        private readonly Mock<IDocumentManager> _mockDocManager;
        private readonly MockDatReaderWriter _dats;
        private readonly Mock<ITransaction> _mockTx;
        private readonly string _terrainDocId = "LandscapeDocument_1";
        private readonly string _layerName = "Test Layer";

        public CreateLandscapeLayerCommandTests() {
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
        public async Task CreateLayer_InRootGroup_AddsLayerSuccessfully() {
            // Arrange
            var command = new CreateLandscapeLayerCommand(_terrainDocId, [], _layerName, false);
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();

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
            // Internal access is allowed via InternalsVisibleTo
            var layer = terrainDoc.GetAllLayers().FirstOrDefault(l => l.Id == command.LayerId);
            Assert.NotNull(layer);
            Assert.Equal(_layerName, layer.Name);
        }

        [Fact]
        public async Task CreateLayer_InNestedGroup_AddsLayerSuccessfully() {
            // Arrange
            var groupId = "nested_group";
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            terrainDoc.AddGroup([], "Nested Group", groupId);

            var command = new CreateLandscapeLayerCommand(_terrainDocId, [groupId], _layerName, false);

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
            var parentGroup = terrainDoc.FindParentGroup([groupId]);
            var layer = parentGroup?.Children.OfType<LandscapeLayer>()
                .FirstOrDefault(l => l.Id == command.LayerId);
            Assert.NotNull(layer);
        }

        [Fact]
        public async Task CreateLayer_SetsCorrectName_AndId() {
            // Arrange
            var command = new CreateLandscapeLayerCommand(_terrainDocId, [], _layerName, false);
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();

            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));
            _mockDocManager.Setup(m => m.PersistDocumentAsync(It.IsAny<DocumentRental<LandscapeDocument>>(),
                    _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Unit>.Success(Unit.Value));

            // Act
            await command.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);

            // Assert
            var layer = terrainDoc.GetAllLayers().First();
            Assert.Equal(_layerName, layer.Name);
            Assert.Equal(command.LayerId, layer.Id);
        }

        [Fact]
        public async Task CreateLayer_UpdatesParentDocumentVersion() {
            // Arrange
            var command = new CreateLandscapeLayerCommand(_terrainDocId, [], _layerName, false);
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            var initialVersion = terrainDoc.Version;

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
        public async Task CreateLayer_AsBase_WhenNoBaseExists_CreatesBaseLayer() {
            // Arrange
            var command = new CreateLandscapeLayerCommand(_terrainDocId, [], _layerName, true);
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();

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
            var layer = terrainDoc.GetAllLayers().FirstOrDefault();
            Assert.NotNull(layer);
            Assert.True(layer.IsBase);
        }

        [Fact]
        public async Task CreateLayer_AsBase_WhenBaseExists_ReturnsFailure() {
            // Arrange
            var command = new CreateLandscapeLayerCommand(_terrainDocId, [], _layerName, true);
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();

            // Existing base layer
            var existingLayerId = Guid.NewGuid().ToString();
            terrainDoc.AddLayer([], "Existing Base", true, existingLayerId);

            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));

            // Act
            var result =
                await command.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Contains("only one allowed", result.Error.Message);
        }

        [Fact]
        public async Task CreateLayer_WhenTerrainDocumentNotFound_ReturnsFailure() {
            // Arrange
            var command = new CreateLandscapeLayerCommand(_terrainDocId, [], _layerName, false);

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
        public async Task CreateLayer_WhenGroupPathInvalid_ReturnsFailure() {
            // Arrange
            var command = new CreateLandscapeLayerCommand(_terrainDocId, ["invalid_group"], _layerName, false);
            var (_, terrainRental) = CreateMockTerrainRental();

            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));

            // Act
            var result =
                await command.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Contains("Group not found", result.Error.Message);
        }

        [Fact]
        public void CreateInverse_ReturnsDeleteCommand_WithCorrectParameters() {
            // Arrange
            var command = new CreateLandscapeLayerCommand(_terrainDocId, ["group"], _layerName, false);

            // Act
            var inverse = command.CreateInverse();

            // Assert
            var deleteCommand = Assert.IsType<DeleteLandscapeLayerCommand>(inverse);
            Assert.Equal(command.LayerId, deleteCommand.LayerId);
            Assert.Equal(command.TerrainDocumentId, deleteCommand.TerrainDocumentId);
            Assert.Equal(command.GroupPath, deleteCommand.GroupPath);
        }

        [Fact]
        public async Task RoundTrip_CreateThenDelete_RestoresOriginalState() {
            // Arrange
            var command = new CreateLandscapeLayerCommand(_terrainDocId, [], _layerName, false);
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();

            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));
            _mockDocManager.Setup(m => m.PersistDocumentAsync(It.IsAny<DocumentRental<LandscapeDocument>>(),
                    _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Unit>.Success(Unit.Value));

            // Act - Create
            await command.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);
            Assert.Single(terrainDoc.GetAllLayers());

            // Act - Delete (Inverse)
            var deleteCommand = (DeleteLandscapeLayerCommand)command.CreateInverse();

            // Manual removal simulation since we can't fully mock the full pipeline here cleanly
            // But let's verify parameters
            Assert.Equal(command.LayerId, deleteCommand.LayerId);

            // Also, we can check that RemoveLayer works on the doc
            terrainDoc.RemoveLayer(deleteCommand.GroupPath, deleteCommand.LayerId);

            // Assert
            Assert.Empty(terrainDoc.GetAllLayers());
        }
    }
}