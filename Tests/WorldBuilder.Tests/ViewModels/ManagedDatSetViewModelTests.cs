using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;
using Xunit;

namespace WorldBuilder.Tests.ViewModels {
    public class ManagedDatSetViewModelTests {
        private readonly Mock<IDatRepositoryService> _mockDatRepo;
        private readonly ManagedDatSet _model;
        private readonly ManagedDatSetViewModel _viewModel;

        public ManagedDatSetViewModelTests() {
            _mockDatRepo = new Mock<IDatRepositoryService>();
            _model = new ManagedDatSet { Id = Guid.NewGuid(), FriendlyName = "Initial Name" };
            _viewModel = new ManagedDatSetViewModel(_model, _mockDatRepo.Object, new NullLogger<ManageDatsViewModel>());
        }

        [Fact]
        public void StartEditCommand_SetsIsEditingTrue() {
            // Act
            _viewModel.StartEditCommand.Execute(null);

            // Assert
            Assert.True(_viewModel.IsEditing);
        }

        [Fact]
        public void CancelEditCommand_ResetsFriendlyNameAndIsEditing() {
            // Arrange
            _viewModel.IsEditing = true;
            _viewModel.FriendlyName = "Modified Name";

            // Act
            _viewModel.CancelEditCommand.Execute(null);

            // Assert
            Assert.False(_viewModel.IsEditing);
            Assert.Equal("Initial Name", _viewModel.FriendlyName);
        }

        [Fact]
        public async Task SaveEditCommand_UpdatesNameOnSuccess() {
            // Arrange
            _viewModel.IsEditing = true;
            _viewModel.FriendlyName = "New Name";
            _mockDatRepo.Setup(r => r.UpdateFriendlyNameAsync(_model.Id, "New Name", It.IsAny<CancellationToken>()))
                .ReturnsAsync(WorldBuilder.Shared.Lib.Result<WorldBuilder.Shared.Lib.Unit>.Success(WorldBuilder.Shared.Lib.Unit.Value));

            // Act
            await _viewModel.SaveEditCommand.ExecuteAsync(null);

            // Assert
            Assert.False(_viewModel.IsEditing);
            _mockDatRepo.Verify(r => r.UpdateFriendlyNameAsync(_model.Id, "New Name", It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
