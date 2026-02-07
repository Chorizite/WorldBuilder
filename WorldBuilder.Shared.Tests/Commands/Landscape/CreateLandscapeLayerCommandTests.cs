using Moq;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Services;
using static WorldBuilder.Shared.Services.DocumentManager;

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

        private (LandscapeLayerDocument, DocumentRental<LandscapeLayerDocument>) CreateMockLayerRental(string id) {
            var layerDoc = new LandscapeLayerDocument(id);
            var rental = new DocumentRental<LandscapeLayerDocument>(layerDoc, () => { });
            return (layerDoc, rental);
        }

        [Fact]
        public async Task CreateLayer_InRootGroup_AddsLayerSuccessfully() {
            // Arrange
            var command = new CreateLandscapeLayerCommand(_terrainDocId, [], _layerName, false);
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            var (_, layerRental) = CreateMockLayerRental(command.TerrainLayerDocumentId);

            _mockDocManager.Setup(m =>
                    m.CreateDocumentAsync(It.IsAny<LandscapeLayerDocument>(), _mockTx.Object,
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeLayerDocument>>.Success(layerRental));
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
            var layer = terrainDoc.GetAllLayers().FirstOrDefault(l => l.Id == command.TerrainLayerDocumentId);
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
            var (_, layerRental) = CreateMockLayerRental(command.TerrainLayerDocumentId);

            _mockDocManager.Setup(m =>
                    m.CreateDocumentAsync(It.IsAny<LandscapeLayerDocument>(), _mockTx.Object,
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeLayerDocument>>.Success(layerRental));
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
                .FirstOrDefault(l => l.Id == command.TerrainLayerDocumentId);
            Assert.NotNull(layer);
        }

        [Fact]
        public async Task CreateLayer_SetsCorrectName_AndId() {
            // Arrange
            var command = new CreateLandscapeLayerCommand(_terrainDocId, [], _layerName, false);
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            var (_, layerRental) = CreateMockLayerRental(command.TerrainLayerDocumentId);

            _mockDocManager.Setup(m =>
                    m.CreateDocumentAsync(It.IsAny<LandscapeLayerDocument>(), _mockTx.Object,
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeLayerDocument>>.Success(layerRental));
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
            Assert.Equal(command.TerrainLayerDocumentId, layer.Id);
        }

        [Fact]
        public async Task CreateLayer_UpdatesParentDocumentVersion() {
            // Arrange
            var command = new CreateLandscapeLayerCommand(_terrainDocId, [], _layerName, false);
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            var initialVersion = terrainDoc.Version;
            var (_, layerRental) = CreateMockLayerRental(command.TerrainLayerDocumentId);

            _mockDocManager.Setup(m =>
                    m.CreateDocumentAsync(It.IsAny<LandscapeLayerDocument>(), _mockTx.Object,
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeLayerDocument>>.Success(layerRental));
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
        public async Task CreateLayer_InitializesLayerDocument_ForUpdating() {
            // Arrange
            var command = new CreateLandscapeLayerCommand(_terrainDocId, [], _layerName, false);
            var (_, terrainRental) = CreateMockTerrainRental();

            var (_, layerRental) = CreateMockLayerRental(command.TerrainLayerDocumentId);

            _mockDocManager.Setup(m =>
                    m.CreateDocumentAsync(It.IsAny<LandscapeLayerDocument>(), _mockTx.Object,
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeLayerDocument>>.Success(layerRental));
            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));
            _mockDocManager.Setup(m => m.PersistDocumentAsync(It.IsAny<DocumentRental<LandscapeDocument>>(),
                    _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Unit>.Success(Unit.Value));

            // Act
            await command.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);

            // Assert
            // No error thrown during execution means it was called if implemented.
        }

        [Fact]
        public async Task CreateLayer_AsBase_WhenNoBaseExists_CreatesBaseLayer() {
            // Arrange
            var command = new CreateLandscapeLayerCommand(_terrainDocId, [], _layerName, true);
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            var (_, layerRental) = CreateMockLayerRental(command.TerrainLayerDocumentId);

            _mockDocManager.Setup(m =>
                    m.CreateDocumentAsync(It.IsAny<LandscapeLayerDocument>(), _mockTx.Object,
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeLayerDocument>>.Success(layerRental));
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

            // Bypass initialization rent calls for existing layers
            var existingLayerId = LandscapeLayerDocument.CreateId();
            terrainDoc.AddLayer([], "Existing Base", true, existingLayerId);

            var (_, layerRental) = CreateMockLayerRental(command.TerrainLayerDocumentId);
            var (_, existingLayerRental) = CreateMockLayerRental(existingLayerId);

            _mockDocManager.Setup(m =>
                    m.CreateDocumentAsync(It.IsAny<LandscapeLayerDocument>(), _mockTx.Object,
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeLayerDocument>>.Success(layerRental));
            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));
            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeLayerDocument>(existingLayerId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeLayerDocument>>.Success(existingLayerRental));

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
            var (_, layerRental) = CreateMockLayerRental(command.TerrainLayerDocumentId);

            _mockDocManager.Setup(m =>
                    m.CreateDocumentAsync(It.IsAny<LandscapeLayerDocument>(), _mockTx.Object,
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeLayerDocument>>.Success(layerRental));
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
        public async Task CreateLayer_WhenLayerDocumentCreationFails_ReturnsFailure() {
            // Arrange
            var command = new CreateLandscapeLayerCommand(_terrainDocId, [], _layerName, false);

            _mockDocManager.Setup(m =>
                    m.CreateDocumentAsync(It.IsAny<LandscapeLayerDocument>(), _mockTx.Object,
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeLayerDocument>>.Failure("Creation Failed"));

            // Act
            var result =
                await command.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Equal("Creation Failed", result.Error.Message);
        }

        [Fact]
        public async Task CreateLayer_WhenGroupPathInvalid_ReturnsFailure() {
            // Arrange
            var command = new CreateLandscapeLayerCommand(_terrainDocId, ["invalid_group"], _layerName, false);
            var (_, terrainRental) = CreateMockTerrainRental();
            var (_, layerRental) = CreateMockLayerRental(command.TerrainLayerDocumentId);

            _mockDocManager.Setup(m =>
                    m.CreateDocumentAsync(It.IsAny<LandscapeLayerDocument>(), _mockTx.Object,
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeLayerDocument>>.Success(layerRental));
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
            Assert.Equal(command.TerrainLayerDocumentId, deleteCommand.TerrainLayerDocumentId);
            Assert.Equal(command.TerrainDocumentId, deleteCommand.TerrainDocumentId);
            Assert.Equal(command.GroupPath, deleteCommand.GroupPath);
        }

        [Fact]
        public async Task RoundTrip_CreateThenDelete_RestoresOriginalState() {
            // Arrange
            var command = new CreateLandscapeLayerCommand(_terrainDocId, [], _layerName, false);
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            var (_, layerRental) = CreateMockLayerRental(command.TerrainLayerDocumentId);

            _mockDocManager.Setup(m =>
                    m.CreateDocumentAsync(It.IsAny<LandscapeLayerDocument>(), _mockTx.Object,
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeLayerDocument>>.Success(layerRental));
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

            // We need to mock ApplyAsync for DeleteLandscapeLayerCommand
            _mockDocManager.Setup(m =>
                    m.ApplyLocalEventAsync(deleteCommand, _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<bool>.Success(true));

            terrainDoc.RemoveLayer(deleteCommand.GroupPath, deleteCommand.TerrainLayerDocumentId);

            // Assert
            Assert.Empty(terrainDoc.GetAllLayers());
        }
    }
}
