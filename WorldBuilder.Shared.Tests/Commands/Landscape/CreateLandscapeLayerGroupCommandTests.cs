using Moq;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Services;
using static WorldBuilder.Shared.Services.DocumentManager;

using WorldBuilder.Shared.Tests.Mocks;

namespace WorldBuilder.Shared.Tests.Commands.Landscape {
    public class CreateLandscapeLayerGroupCommandTests {
        private readonly Mock<IDocumentManager> _mockDocManager;
        private readonly MockDatReaderWriter _dats;
        private readonly Mock<ITransaction> _mockTx;
        private readonly string _terrainDocId = "LandscapeDocument_1";
        private readonly string _groupName = "Test Group";

        public CreateLandscapeLayerGroupCommandTests() {
            _mockDocManager = new Mock<IDocumentManager>();
            _dats = new MockDatReaderWriter();
            _mockTx = new Mock<ITransaction>();
        }

        private (LandscapeDocument, DocumentRental<LandscapeDocument>) CreateMockTerrainRental() {
            var terrainDoc = new LandscapeDocument(_terrainDocId);
            var rental = new DocumentRental<LandscapeDocument>(terrainDoc, () => { });
            return (terrainDoc, rental);
        }

        #region Successful Creation Tests

        [Fact]
        public async Task CreateGroup_InRootGroup_AddsGroupSuccessfully() {
            // Arrange
            var command = new CreateLandscapeLayerGroupCommand(_terrainDocId, [], _groupName);
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
            var groups = terrainDoc.GetAllLayersAndGroups().OfType<LandscapeLayerGroup>().ToList();
            Assert.Single(groups);
            Assert.Equal(_groupName, groups[0].Name);
        }

        [Fact]
        public async Task CreateGroup_InNestedGroup_AddsGroupSuccessfully() {
            // Arrange
            var parentGroupId = "parent_group";
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            terrainDoc.AddGroup([], "Parent Group", parentGroupId);

            var command = new CreateLandscapeLayerGroupCommand(_terrainDocId, [parentGroupId], _groupName);

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
            var parentGroup = terrainDoc.FindParentGroup([parentGroupId]);
            Assert.NotNull(parentGroup);
            var nestedGroups = parentGroup.Children.OfType<LandscapeLayerGroup>().ToList();
            Assert.Single(nestedGroups);
            Assert.Equal(_groupName, nestedGroups[0].Name);
        }

        [Fact]
        public async Task CreateGroup_InDeeplyNestedPath_AddsGroupSuccessfully() {
            // Arrange
            var grandparentGroupId = "grandparent_group";
            var parentGroupId = "parent_group";
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            terrainDoc.AddGroup([], "Grandparent Group", grandparentGroupId);
            terrainDoc.AddGroup([grandparentGroupId], "Parent Group", parentGroupId);

            var command =
                new CreateLandscapeLayerGroupCommand(_terrainDocId, [grandparentGroupId, parentGroupId], _groupName);

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
            var parentGroup = terrainDoc.FindParentGroup([grandparentGroupId, parentGroupId]);
            Assert.NotNull(parentGroup);
            var nestedGroups = parentGroup.Children.OfType<LandscapeLayerGroup>().ToList();
            Assert.Single(nestedGroups);
            Assert.Equal(_groupName, nestedGroups[0].Name);
        }

        [Fact]
        public async Task CreateGroup_SetsCorrectName_AndId() {
            // Arrange
            var command = new CreateLandscapeLayerGroupCommand(_terrainDocId, [], _groupName);
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
            var group = terrainDoc.GetAllLayersAndGroups().OfType<LandscapeLayerGroup>().First();
            Assert.Equal(_groupName, group.Name);
            Assert.Equal(command.GroupId, group.Id);
        }

        [Fact]
        public async Task CreateGroup_UpdatesParentDocumentVersion() {
            // Arrange
            var command = new CreateLandscapeLayerGroupCommand(_terrainDocId, [], _groupName);
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
        public async Task CreateMultipleGroups_InSameParent_AllExist() {
            // Arrange
            var (terrainDoc, terrainRental) = CreateMockTerrainRental();
            var command1 = new CreateLandscapeLayerGroupCommand(_terrainDocId, [], "Group 1");
            var command2 = new CreateLandscapeLayerGroupCommand(_terrainDocId, [], "Group 2");
            var command3 = new CreateLandscapeLayerGroupCommand(_terrainDocId, [], "Group 3");

            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));
            _mockDocManager.Setup(m => m.PersistDocumentAsync(It.IsAny<DocumentRental<LandscapeDocument>>(),
                    _mockTx.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Unit>.Success(Unit.Value));

            // Act
            await command1.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);
            await command2.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);
            await command3.ApplyResultAsync(_mockDocManager.Object, _dats, _mockTx.Object, default);

            // Assert
            var groups = terrainDoc.GetAllLayersAndGroups().OfType<LandscapeLayerGroup>().ToList();
            Assert.Equal(3, groups.Count);
            Assert.Contains(groups, g => g.Name == "Group 1");
            Assert.Contains(groups, g => g.Name == "Group 2");
            Assert.Contains(groups, g => g.Name == "Group 3");
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public async Task CreateGroup_WhenTerrainDocumentNotFound_ReturnsFailure() {
            // Arrange
            var command = new CreateLandscapeLayerGroupCommand(_terrainDocId, [], _groupName);

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
        public async Task CreateGroup_WhenGroupPathInvalid_ReturnsFailure() {
            // Arrange
            var command = new CreateLandscapeLayerGroupCommand(_terrainDocId, ["invalid_group"], _groupName);
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
        public async Task CreateGroup_WhenPersistFails_ReturnsFailure() {
            // Arrange
            var command = new CreateLandscapeLayerGroupCommand(_terrainDocId, [], _groupName);
            var (_, terrainRental) = CreateMockTerrainRental();

            _mockDocManager.Setup(m =>
                    m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));
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

        #endregion

        #region Serialization & Construction Tests

        [Fact]
        public void Constructor_WithParameters_SetsPropertiesCorrectly() {
            // Arrange & Act
            var command = new CreateLandscapeLayerGroupCommand(_terrainDocId, ["group1", "group2"], _groupName);

            // Assert
            Assert.Equal(_terrainDocId, command.TerrainDocumentId);
            Assert.Equal(["group1", "group2"], command.GroupPath);
            Assert.Equal(_groupName, command.Name);
            Assert.False(string.IsNullOrEmpty(command.GroupId));
        }

        [Fact]
        public void DefaultConstructor_HasDefaultValues() {
            // Arrange & Act
            var command = new CreateLandscapeLayerGroupCommand();

            // Assert
            Assert.Equal(string.Empty, command.TerrainDocumentId);
            Assert.Empty(command.GroupPath);
            Assert.Equal("New Group", command.Name);
            Assert.False(string.IsNullOrEmpty(command.GroupId));
        }

        [Fact]
        public void GroupId_IsUnique_ForEachInstance() {
            // Arrange & Act
            var command1 = new CreateLandscapeLayerGroupCommand();
            var command2 = new CreateLandscapeLayerGroupCommand();

            // Assert
            Assert.NotEqual(command1.GroupId, command2.GroupId);
        }

        #endregion

        #region CreateInverse Tests

        [Fact]
        public void CreateInverse_ThrowsNotImplementedException() {
            // Arrange
            var command = new CreateLandscapeLayerGroupCommand(_terrainDocId, [], _groupName);

            // Act & Assert
            Assert.Throws<NotImplementedException>(() => command.CreateInverse());
        }

        #endregion
    }
}
