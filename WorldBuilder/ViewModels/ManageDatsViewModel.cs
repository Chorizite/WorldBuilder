using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Lib;
using WorldBuilder.Messages;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;
using HanumanInstitute.MvvmDialogs;
using HanumanInstitute.MvvmDialogs.Avalonia;
using HanumanInstitute.MvvmDialogs.FrameworkDialogs;

namespace WorldBuilder.ViewModels;

/// <summary>
/// View model for a single managed DAT set in the management view.
/// </summary>
public partial class ManagedDatSetViewModel : ObservableObject {
    private readonly ManagedDatSet _model;
    private readonly IDatRepositoryService _datRepository;
    private readonly ILogger _log;

    [ObservableProperty]
    private string _friendlyName;

    [ObservableProperty]
    private bool _isEditing;

    public Guid Id => _model.Id;
    public int PortalIteration => _model.PortalIteration;
    public int CellIteration => _model.CellIteration;
    public int HighResIteration => _model.HighResIteration;
    public int LanguageIteration => _model.LanguageIteration;
    public string CombinedMd5 => _model.CombinedMd5;
    public DateTime ImportDate => _model.ImportDate;

    public string DisplayIteration => $"P:{PortalIteration} C:{CellIteration} H:{HighResIteration} L:{LanguageIteration}";

    public ManagedDatSetViewModel(ManagedDatSet model, IDatRepositoryService datRepository, ILogger log) {
        _model = model;
        _datRepository = datRepository;
        _log = log;
        _friendlyName = model.FriendlyName;
    }

    [RelayCommand]
    private void StartEdit() => IsEditing = true;

    [RelayCommand]
    private async Task SaveEdit() {
        if (string.IsNullOrWhiteSpace(FriendlyName)) {
            FriendlyName = _model.FriendlyName;
            IsEditing = false;
            return;
        }

        var result = await _datRepository.UpdateFriendlyNameAsync(Id, FriendlyName, CancellationToken.None);
        if (result.IsSuccess) {
            IsEditing = false;
        }
        else {
            _log.LogError("Failed to update friendly name: {Error}", result.Error.Message);
        }
    }

    [RelayCommand]
    private void CancelEdit() {
        FriendlyName = _model.FriendlyName;
        IsEditing = false;
    }
}

/// <summary>
/// View model for the manage DATs screen.
/// </summary>
public partial class ManageDatsViewModel : SplashPageViewModelBase {
    private readonly ILogger<ManageDatsViewModel> _log;
    private readonly WorldBuilderSettings _settings;
    private readonly IDatRepositoryService _datRepository;
    private readonly IDialogService _dialogService;

    public ObservableCollection<ManagedDatSetViewModel> ManagedDataSets { get; } = [];

    [ObservableProperty]
    private ManagedDatSetViewModel? _selectedSet;

    public ManageDatsViewModel(WorldBuilderSettings settings, ILogger<ManageDatsViewModel> log, IDatRepositoryService datRepository, IDialogService dialogService) {
        _settings = settings;
        _log = log;
        _datRepository = datRepository;
        _dialogService = dialogService;

        RefreshList();
    }

    private void RefreshList() {
        ManagedDataSets.Clear();
        foreach (var set in _datRepository.GetManagedDataSets()) {
            ManagedDataSets.Add(new ManagedDatSetViewModel(set, _datRepository, _log));
        }
    }

    [RelayCommand]
    private async Task Import() {
        var suggestedPath = string.IsNullOrEmpty(_settings.App.LastBaseDatDirectory) ? _settings.App.ProjectsDirectory : _settings.App.LastBaseDatDirectory;
        var folders = await TopLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions() {
            Title = "Choose DAT directory to import",
            AllowMultiple = false,
            SuggestedStartLocation = await TopLevel.StorageProvider.TryGetFolderFromPathAsync(suggestedPath)
        });

        if (folders.Count == 0) return;

        var localPath = folders[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(localPath)) return;

        IsLoading = true;
        LoadingStatus = "Calculating hashes...";
        LoadingProgress = 0f;

        var progress = new Progress<(string message, float progress)>(p => {
            LoadingStatus = p.message;
            LoadingProgress = p.progress * 100f;
        });

        var result = await _datRepository.ImportAsync(localPath, null, progress, CancellationToken.None);
        
        IsLoading = false;

        if (result.IsSuccess) {
            _settings.App.LastBaseDatDirectory = localPath;
            _settings.Save();
            RefreshList();
        }
        else {
            await _dialogService.ShowMessageBoxAsync(null, result.Error.Message, "Import Failed");
        }
    }

    [RelayCommand]
    private async Task Remove(ManagedDatSetViewModel? setVM) {
        if (setVM == null) return;

        var confirm = await _dialogService.ShowMessageBoxAsync(null, 
            $"Are you sure you want to remove the DAT set '{setVM.FriendlyName}'?\n\n" +
            "WARNING: Any projects using this DAT set will no longer be able to open!",
            "Confirm Removal",
            MessageBoxButton.YesNo);

        if (confirm == true) {
            IsLoading = true;
            LoadingStatus = "Deleting files...";
            var result = await _datRepository.DeleteAsync(setVM.Id, CancellationToken.None);
            IsLoading = false;

            if (result.IsSuccess) {
                RefreshList();
            }
            else {
                await _dialogService.ShowMessageBoxAsync(null, result.Error.Message, "Delete Failed");
            }
        }
    }

    [RelayCommand]
    private void GoBack() {
        WeakReferenceMessenger.Default.Send(new SplashPageChangedMessage(SplashPageViewModel.SplashPage.ProjectSelection));
    }
}
