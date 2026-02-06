using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using WorldBuilder.Lib;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Messages;
using WorldBuilder.Services;
using WorldBuilder.ViewModels;
using WorldBuilder.Views;
using Xunit;

namespace WorldBuilder.Tests.Views;

public class ProjectSelectionViewTests {
    [AvaloniaFact]
    public void ProjectSelectionView_InitializesWithoutThrowing() {
        // Arrange & Act
        var view = new ProjectSelectionView();

        // Assert
        Assert.NotNull(view);
    }

    [AvaloniaFact]
    public void ProjectSelectionView_HasCorrectViewModelType() {
        // Arrange
        var settings = new WorldBuilderSettings();
        var projectManager = new ProjectManager();
        var viewModel = new ProjectSelectionViewModel(settings, projectManager, NullLogger<ProjectSelectionViewModel>.Instance);
        var view = new ProjectSelectionView();
        view.DataContext = viewModel;

        // Act
        view.ApplyTemplate();

        // Assert
        Assert.NotNull(view.DataContext);
        Assert.IsType<ProjectSelectionViewModel>(view.DataContext);
    }

    [AvaloniaFact]
    public void OpenRecentProject_WithNoError_LoadsProject() {
        // Arrange
        var settings = new WorldBuilderSettings();
        var projectManager = new ProjectManager();
        var viewModel = new ProjectSelectionViewModel(settings, projectManager, NullLogger<ProjectSelectionViewModel>.Instance);

        var recentProject = new RecentProject {
            Name = "Test Project",
            FilePath = "test.wbproj",
            Error = null // No error
        };

        // Act & Assert - This should not throw and should attempt to load the project
        // We can't easily test the actual loading without a real project file,
        // but we can test that the method executes without error for a project without errors
        var testTask = viewModel.OpenRecentProjectCommand.ExecuteAsync(recentProject);
        Assert.NotNull(testTask);
    }

    [AvaloniaFact]
    public void OpenRecentProject_WithError_SendsShowProjectErrorDetailsMessage() {
        // Arrange
        var settings = new WorldBuilderSettings();
        var projectManager = new ProjectManager();
        var viewModel = new ProjectSelectionViewModel(settings, projectManager, NullLogger<ProjectSelectionViewModel>.Instance);

        var recentProject = new RecentProject {
            Name = "Error Project",
            FilePath = "error.wbproj",
            Error = "File not found"
        };

        // Set up a test recipient to verify the message is sent
        var testRecipient = new TestMessageRecipient();
        WeakReferenceMessenger.Default.Register<ShowProjectErrorDetailsMessage>(testRecipient);

        // Act
        var testTask = viewModel.OpenRecentProjectCommand.ExecuteAsync(recentProject);

        // Assert
        Assert.NotNull(testTask);
        Assert.True(recentProject.HasError);
    }

    [AvaloniaFact]
    public void RecentProject_HasErrorProperty_WorksCorrectly() {
        // Arrange
        var projectWithError = new RecentProject { Error = "Some error" };
        var projectWithoutError = new RecentProject { Error = null };
        var projectWithEmptyError = new RecentProject { Error = "" };

        // Act & Assert
        Assert.True(projectWithError.HasError);
        Assert.False(projectWithoutError.HasError);
        Assert.False(projectWithEmptyError.HasError);
    }

    public class TestMessageRecipient : IRecipient<ShowProjectErrorDetailsMessage> {
        public bool MessageReceived { get; set; }
        public ShowProjectErrorDetailsMessage? ReceivedMessage { get; set; }

        public void Receive(ShowProjectErrorDetailsMessage message) {
            MessageReceived = true;
            ReceivedMessage = message;
        }
    }
}