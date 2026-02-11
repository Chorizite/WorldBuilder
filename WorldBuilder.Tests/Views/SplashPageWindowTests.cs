using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using WorldBuilder.Lib.Factories;
using WorldBuilder.Messages;
using WorldBuilder.ViewModels;
using WorldBuilder.Views;
using Xunit;

namespace WorldBuilder.Tests.Views;

public class SplashPageWindowTests {
    [AvaloniaFact]
    public void SplashPageWindow_InitializesWithoutThrowing() {
        // Arrange & Act
        var window = new SplashPageWindow();

        // Assert
        Assert.NotNull(window);
    }

    [AvaloniaFact]
    public void SplashPageWindow_HasCorrectViewModelType() {
        // Arrange
        var splashFactory = new SplashPageFactory();
        var viewModel = new SplashPageViewModel(splashFactory, NullLogger<SplashPageViewModel>.Instance);
        var window = new SplashPageWindow();
        window.DataContext = viewModel;

        // Act
        window.ApplyTemplate();

        // Assert
        Assert.NotNull(window.DataContext);
        Assert.IsType<SplashPageViewModel>(window.DataContext);
    }

    [AvaloniaFact]
    public void SplashPageWindow_InitializesWithProjectSelectionPage() {
        // Arrange
        var splashFactory = new SplashPageFactory();
        var viewModel = new SplashPageViewModel(splashFactory, NullLogger<SplashPageViewModel>.Instance);
        var window = new SplashPageWindow();
        window.DataContext = viewModel;
        window.ApplyTemplate();

        // Act
        var currentPage = viewModel.CurrentPage;

        // Assert
        Assert.NotNull(currentPage);
        Assert.IsType<ProjectSelectionViewModel>(currentPage);
    }

    [AvaloniaFact]
    public void SplashPageWindow_UpdatesContentWhenViewModelChanges() {
        // Arrange
        var splashFactory = new SplashPageFactory();
        var viewModel = new SplashPageViewModel(splashFactory, NullLogger<SplashPageViewModel>.Instance);
        var window = new SplashPageWindow();
        window.DataContext = viewModel;
        window.ApplyTemplate();

        // Act - Change the current page via the view model
        viewModel.OpenProjectSelectionCommand.Execute(null);

        // Assert
        Assert.NotNull(viewModel.CurrentPage);
        Assert.IsType<ProjectSelectionViewModel>(viewModel.CurrentPage);
    }

    [AvaloniaFact]
    public void SplashPageWindow_NavigatesToCreateProjectPage() {
        // Arrange
        var splashFactory = new SplashPageFactory();
        var viewModel = new SplashPageViewModel(splashFactory, NullLogger<SplashPageViewModel>.Instance);
        var window = new SplashPageWindow();
        window.DataContext = viewModel;
        window.ApplyTemplate();

        // Act - Send message to navigate to CreateProject page
        WeakReferenceMessenger.Default.Send(new SplashPageChangedMessage(SplashPageViewModel.SplashPage.CreateProject));

        // Assert
        Assert.NotNull(viewModel.CurrentPage);
        Assert.IsType<CreateProjectViewModel>(viewModel.CurrentPage);
    }

    [AvaloniaFact]
    public void SplashPageWindow_NavigatesToProjectSelectionPage() {
        // Arrange
        var splashFactory = new SplashPageFactory();
        var viewModel = new SplashPageViewModel(splashFactory, NullLogger<SplashPageViewModel>.Instance);
        var window = new SplashPageWindow();
        window.DataContext = viewModel;
        window.ApplyTemplate();

        // Act - First navigate to CreateProject, then back to ProjectSelection
        WeakReferenceMessenger.Default.Send(new SplashPageChangedMessage(SplashPageViewModel.SplashPage.CreateProject));
        WeakReferenceMessenger.Default.Send(new SplashPageChangedMessage(SplashPageViewModel.SplashPage.ProjectSelection));

        // Assert
        Assert.NotNull(viewModel.CurrentPage);
        Assert.IsType<ProjectSelectionViewModel>(viewModel.CurrentPage);
    }

    [AvaloniaFact]
    public void SplashPageWindow_ViewModelNavigationCommandsWork() {
        // Arrange
        var splashFactory = new SplashPageFactory();
        var viewModel = new SplashPageViewModel(splashFactory, NullLogger<SplashPageViewModel>.Instance);
        var window = new SplashPageWindow();
        window.DataContext = viewModel;
        window.ApplyTemplate();

        // Act - Use the view model's command to navigate
        viewModel.OpenProjectSelectionCommand.Execute(null);

        // Assert
        Assert.NotNull(viewModel.CurrentPage);
        Assert.IsType<ProjectSelectionViewModel>(viewModel.CurrentPage);
    }

    [AvaloniaFact]
    public void SplashPageWindow_BindingContextUpdatesCorrectly() {
        // Arrange
        var splashFactory = new SplashPageFactory();
        var viewModel = new SplashPageViewModel(splashFactory, NullLogger<SplashPageViewModel>.Instance);
        var window = new SplashPageWindow();
        window.DataContext = viewModel;
        window.ApplyTemplate();

        // Act - Navigate to CreateProject page
        var createProjectVM = splashFactory.Create<CreateProjectViewModel>();
        viewModel.CurrentPage = createProjectVM;

        // Assert
        Assert.Same(createProjectVM, viewModel.CurrentPage);
        // The TransitioningContentControl is bound to CurrentPage, so its content should be the view model
        var contentControl = window.Content as TransitioningContentControl;
        if (contentControl == null) {
            contentControl = window.GetVisualDescendants()
                .OfType<TransitioningContentControl>()
                .FirstOrDefault();
        }
        Assert.NotNull(contentControl);
        Assert.Equal(createProjectVM, contentControl.Content);
    }

    [AvaloniaFact]
    public void SplashPageWindow_ViewModelDoesNotBeNullAfterRapidNavigation() {
        // Arrange
        var splashFactory = new SplashPageFactory();
        var viewModel = new SplashPageViewModel(splashFactory, NullLogger<SplashPageViewModel>.Instance);
        var window = new SplashPageWindow();
        window.DataContext = viewModel;
        window.ApplyTemplate();

        // Act - Rapidly switch between pages
        WeakReferenceMessenger.Default.Send(new SplashPageChangedMessage(SplashPageViewModel.SplashPage.CreateProject));
        WeakReferenceMessenger.Default.Send(new SplashPageChangedMessage(SplashPageViewModel.SplashPage.ProjectSelection));
        WeakReferenceMessenger.Default.Send(new SplashPageChangedMessage(SplashPageViewModel.SplashPage.CreateProject));
        WeakReferenceMessenger.Default.Send(new SplashPageChangedMessage(SplashPageViewModel.SplashPage.ProjectSelection));

        // Assert
        Assert.NotNull(viewModel.CurrentPage);
        Assert.IsType<ProjectSelectionViewModel>(viewModel.CurrentPage);
    }

    [AvaloniaFact]
    public void SplashPageWindow_ViewModelMaintainsStateDuringNavigation() {
        // Arrange
        var splashFactory = new SplashPageFactory();
        var viewModel = new SplashPageViewModel(splashFactory, NullLogger<SplashPageViewModel>.Instance);
        var window = new SplashPageWindow();
        window.DataContext = viewModel;
        window.ApplyTemplate();

        // Store initial state
        var initialCurrentPage = viewModel.CurrentPage;

        // Act - Navigate to another page and back
        WeakReferenceMessenger.Default.Send(new SplashPageChangedMessage(SplashPageViewModel.SplashPage.CreateProject));
        WeakReferenceMessenger.Default.Send(new SplashPageChangedMessage(SplashPageViewModel.SplashPage.ProjectSelection));

        // Assert
        Assert.NotNull(viewModel.CurrentPage);
        Assert.IsType<ProjectSelectionViewModel>(viewModel.CurrentPage);
        // The page should be recreated by the factory, so it shouldn't be the same instance
        Assert.NotSame(initialCurrentPage, viewModel.CurrentPage);
    }
}