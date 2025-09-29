using Avalonia.Animation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using System;
using WorldBuilder.Lib.Factories;

namespace WorldBuilder.ViewModels;


public partial class SplashPageViewModel : ViewModelBase, IRecipient<SplashPageChangedMessage> {
    private readonly ILogger<SplashPageViewModel> _log;
    private readonly SplashPageFactory _splashFactory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnSubPage))]
    private SplashPageViewModelBase _currentPage;

    public bool IsOnSubPage => CurrentPage is not ProjectSelectionViewModel;

    public enum SplashPage { ProjectSelection, CreateProject };

    public SplashPageViewModel(SplashPageFactory splashFactory, ILogger<SplashPageViewModel> log) {
        _log = log;
        _splashFactory = splashFactory;

        CurrentPage = GetPage(SplashPage.ProjectSelection);
        WeakReferenceMessenger.Default.RegisterAll(this);
    }

    [RelayCommand]
    private void OpenProjectSelection() {
        CurrentPage = GetPage(SplashPage.ProjectSelection);
    }

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