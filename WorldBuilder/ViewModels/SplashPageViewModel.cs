using Avalonia.Animation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using WorldBuilder.Lib.Factories;
using WorldBuilder.Messages;

namespace WorldBuilder.ViewModels;

/// <summary>
/// Main view model for the splash screen, managing navigation between different splash pages.
/// </summary>
public partial class SplashPageViewModel : ViewModelBase, IRecipient<SplashPageChangedMessage> {
    private readonly ILogger<SplashPageViewModel> _log;
    private readonly SplashPageFactory _splashFactory;

    /// <summary>
    /// Gets or sets the current splash page view model.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnSubPage))]
    private SplashPageViewModelBase _currentPage;

    /// <summary>
    /// Gets a value indicating whether the current page is a sub page (not the project selection page).
    /// </summary>
    public bool IsOnSubPage => CurrentPage is not ProjectSelectionViewModel;

    /// <summary>
    /// Enumerates the available splash page types.
    /// </summary>
    public enum SplashPage { ProjectSelection, CreateProject };

    /// <summary>
    /// Initializes a new instance of the SplashPageViewModel class for design-time use.
    /// </summary>
    // only used for design time
    public SplashPageViewModel() {
        _splashFactory = new SplashPageFactory();
        _log = new NullLogger<SplashPageViewModel>();
        CurrentPage = new ProjectSelectionViewModel();
    }

    /// <summary>
    /// Initializes a new instance of the SplashPageViewModel class with the specified dependencies.
    /// </summary>
    /// <param name="splashFactory">The splash page factory</param>
    /// <param name="log">The logger instance</param>
    public SplashPageViewModel(SplashPageFactory splashFactory, ILogger<SplashPageViewModel> log) {
        _log = log;
        _splashFactory = splashFactory;

        CurrentPage = GetPage(SplashPage.ProjectSelection);
        WeakReferenceMessenger.Default.Register<SplashPageChangedMessage>(this);
    }

    /// <summary>
    /// Opens the project selection page.
    /// </summary>
    [RelayCommand]
    private void OpenProjectSelection() {
        CurrentPage = GetPage(SplashPage.ProjectSelection);
    }

    /// <summary>
    /// Handles the splash page change message.
    /// </summary>
    /// <param name="message">The message containing the target splash page</param>
    public void Receive(SplashPageChangedMessage message) {
        CurrentPage = GetPage(message.Value);
    }

    private SplashPageViewModelBase GetPage(SplashPage value) {
        return value switch {
            SplashPage.ProjectSelection => _splashFactory.Create<ProjectSelectionViewModel>(),
            SplashPage.CreateProject => _splashFactory.Create<CreateProjectViewModel>(),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
        };
    }


}