using Moq;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Services;
using WorldBuilder.Shared.Tests.Mocks;

namespace WorldBuilder.Shared.Tests.Commands.Landscape {
    public class DeleteLandscapeLayerCommandTests {
        private readonly Mock<IDocumentManager> _mockDocManager;
        private readonly MockDatReaderWriter _dats;
        private readonly Mock<ITransaction> _mockTx;
        private readonly string _terrainDocId = "LandscapeDocument_1";

        public DeleteLandscapeLayerCommandTests() {
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
        public async Task DeleteLayer_FromRootGroup_RemovesLayerSuccessfully() {
            // Arrange
            var layerId = Guid.NewGuid().ToString();
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            terrainDoc.AddLayer([], "Test Layer", false, layerId);

            var command = new DeleteLandscapeLayerCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = layerId,
                GroupPath = []
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
            Assert.Empty(terrainDoc.GetAllLayers());
        }

        [Fact]
        public async Task DeleteLayer_FromNestedGroup_RemovesLayerSuccessfully() {
            // Arrange
            var groupId = "nested_group";
            var layerId = Guid.NewGuid().ToString();
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            terrainDoc.AddGroup([], "Nested Group", groupId);
            terrainDoc.AddLayer([groupId], "Test Layer", false, layerId);

            var command = new DeleteLandscapeLayerCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = layerId,
                GroupPath = [groupId]
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
            var parentGroup = terrainDoc.FindParentGroup([groupId]);
            Assert.Empty(parentGroup!.Children.OfType<LandscapeLayer>());
        }

        [Fact]
        public async Task DeleteLayer_UpdatesParentDocumentVersion() {
            // Arrange
            var layerId = Guid.NewGuid().ToString();
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            terrainDoc.AddLayer([], "Test Layer", false, layerId);
            var initialVersion = terrainDoc.Version;

            var command = new DeleteLandscapeLayerCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = layerId,
                GroupPath = []
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
        public async Task DeleteLayer_WhenIsBaseLayer_ThrowsException() {
            // Arrange
            var layerId = Guid.NewGuid().ToString();
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            terrainDoc.AddLayer([], "Base Layer", true, layerId);

            var command = new DeleteLandscapeLayerCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = layerId,
                GroupPath = []
            };

            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));

            // Act
            var result =
                await command.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Contains("Cannot remove the base layer", result.Error.Message);
        }

        [Fact]
        public async Task DeleteLayer_WhenTerrainDocumentNotFound_ReturnsFailure() {
            // Arrange
            var layerId = Guid.NewGuid().ToString();
            var command = new DeleteLandscapeLayerCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = layerId,
                GroupPath = []
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
        public async Task DeleteLayer_WhenLayerNotFound_ThrowsException() {
            // Arrange
            var layerId = Guid.NewGuid().ToString();
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();

            var command = new DeleteLandscapeLayerCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = layerId, // This ID is not in the document
                GroupPath = []
            };

            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));

            // Act
            var result =
                await command.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Contains("Layer not found", result.Error.Message);
        }

        [Fact]
        public async Task DeleteLayer_WhenGroupPathInvalid_ThrowsException() {
            // Arrange
            var layerId = Guid.NewGuid().ToString();
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            terrainDoc.AddLayer([], "Test Layer", false, layerId);

            var command = new DeleteLandscapeLayerCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = layerId,
                GroupPath = ["invalid_group"]
            };

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
        public void CreateInverse_ReturnsCreateCommand_WithCorrectParameters() {
            // Arrange
            var layerId = Guid.NewGuid().ToString();
            var command = new DeleteLandscapeLayerCommand {
                TerrainDocumentId = _terrainDocId,
                LayerId = layerId,
                GroupPath = ["group"]
            };

            // Act
            var inverse = command.CreateInverse();

            // Assert
            var createCommand = Assert.IsType<CreateLandscapeLayerCommand>(inverse);
            Assert.Equal(command.LayerId, createCommand.LayerId);
            Assert.Equal(command.TerrainDocumentId, createCommand.TerrainDocumentId);
            Assert.Equal(command.GroupPath, createCommand.GroupPath);
        }
    }
}