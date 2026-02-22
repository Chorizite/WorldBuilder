using Moq;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Services;
using WorldBuilder.Shared.Tests.Mocks;

namespace WorldBuilder.Shared.Tests.Commands.Landscape {
    public class CreateLandscapeDocumentCommandTests {
        private readonly Mock<IDocumentManager> _mockDocManager;
        private readonly MockDatReaderWriter _dats;
        private readonly Mock<ITransaction> _mockTx;
        private readonly uint _regionId = 1;

        public CreateLandscapeDocumentCommandTests() {
            _mockDocManager = new Mock<IDocumentManager>();
            _dats = new MockDatReaderWriter();
            _mockTx = new Mock<ITransaction>();
        }

        [Fact]
        public async Task CreateDocument_WithValidRegionId_CreatesDocumentAndBaseLayer() {
            // Arrange
            var command = new CreateLandscapeDocumentCommand(_regionId);
            var landscapeDocId = LandscapeDocument.GetIdFromRegion(_regionId);
            var landscapeDoc = new LandscapeDocument(_regionId);
            var rental = new DocumentRental<LandscapeDocument>(landscapeDoc, () => { });

            _mockDocManager.Setup(m =>
                    m.CreateDocumentAsync(It.IsAny<LandscapeDocument>(), _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(rental));

            _mockDocManager.Setup(m => m.ApplyLocalEventAsync(It.IsAny<CreateLandscapeLayerCommand>(), _mockTx.Object,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<string>.Success("base_layer_id"));

            _mockDocManager.Setup(m => m.PersistDocumentAsync(It.IsAny<DocumentRental<LandscapeDocument>>(),
                    _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Unit>.Success(Unit.Value));

            // Act
            var result =
                await command.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            _mockDocManager.Verify(
                m => m.CreateDocumentAsync(It.Is<LandscapeDocument>(d => d.Id == landscapeDocId), _mockTx.Object,
                    It.IsAny<CancellationToken>()), Times.Once);
            _mockDocManager.Verify(
                m => m.ApplyLocalEventAsync(It.Is<CreateLandscapeLayerCommand>(c => c.IsBase == true), _mockTx.Object,
                    It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void CreateDocument_SetsCorrectDocumentId_FromRegionId() {
            // Arrange
            var command = new CreateLandscapeDocumentCommand(_regionId);

            // Assert
            Assert.Equal(_regionId, command.RegionId);
        }

        [Fact]
        public async Task CreateDocument_WhenDocumentManagerFails_ReturnsFailure() {
            // Arrange
            var command = new CreateLandscapeDocumentCommand(_regionId);
            _mockDocManager.Setup(m =>
                    m.CreateDocumentAsync(It.IsAny<LandscapeDocument>(), _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Failure("Failed"));

            // Act
            var result =
                await command.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Equal("Failed", result.Error.Message);
        }

        [Fact]
        public async Task CreateDocument_WhenBaseLayerCreationFails_ReturnsFailure() {
            // Arrange
            var command = new CreateLandscapeDocumentCommand(_regionId);
            var landscapeDoc = new LandscapeDocument(_regionId);
            var rental = new DocumentRental<LandscapeDocument>(landscapeDoc, () => { });

            _mockDocManager.Setup(m =>
                    m.CreateDocumentAsync(It.IsAny<LandscapeDocument>(), _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(rental));

            _mockDocManager.Setup(m => m.ApplyLocalEventAsync(It.IsAny<CreateLandscapeLayerCommand>(), _mockTx.Object,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<string>.Failure("Layer Failed"));

            // Act
            var result =
                await command.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Equal("Layer Failed", result.Error.Message);
        }

        [Fact]
        public async Task CreateDocument_WhenPersistFails_ReturnsFailure() {
            // Arrange
            var command = new CreateLandscapeDocumentCommand(_regionId);
            var landscapeDoc = new LandscapeDocument(_regionId);
            var rental = new DocumentRental<LandscapeDocument>(landscapeDoc, () => { });

            _mockDocManager.Setup(m =>
                    m.CreateDocumentAsync(It.IsAny<LandscapeDocument>(), _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(rental));

            _mockDocManager.Setup(m => m.ApplyLocalEventAsync(It.IsAny<CreateLandscapeLayerCommand>(), _mockTx.Object,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<string>.Success("base_layer_id"));

            _mockDocManager.Setup(m => m.PersistDocumentAsync(It.IsAny<DocumentRental<LandscapeDocument>>(),
                    _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Unit>.Failure("Persist Failed"));

            // Act
            var result =
                await command.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Equal("Persist Failed", result.Error.Message);
        }

        [Fact]
        public void CreateInverse_ThrowsNotImplementedException() {
            // Arrange
            var command = new CreateLandscapeDocumentCommand(_regionId);

            // Assert
            Assert.Throws<NotImplementedException>(() => command.CreateInverse());
        }
    }
}