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
/// View model for the manage DATs screen.
/// </summary>
public partial class ManageDatsViewModel : SplashPageViewModelBase, IDisposable {
    private readonly ILogger<ManageDatsViewModel> _log;
    private readonly WorldBuilderSettings _settings;
    private readonly IDatRepositoryService _datRepository;
    private readonly IAceRepositoryService _aceRepository;
    private readonly IKeywordRepositoryService _keywordRepository;
    private readonly IDialogService _dialogService;

    public ObservableCollection<ManagedDatSetViewModel> ManagedDataSets { get; } = [];
    public ObservableCollection<ManagedAceDbViewModel> ManagedAceDbs { get; } = [];
    public ObservableCollection<ManagedKeywordDbViewModel> ManagedKeywordDbs { get; } = [];

    [ObservableProperty]
    private ManagedDatSetViewModel? _selectedSet;

    [ObservableProperty]
    private ManagedAceDbViewModel? _selectedAceDb;

    [ObservableProperty]
    private ManagedKeywordDbViewModel? _selectedKeywordDb;

    public ManageDatsViewModel(WorldBuilderSettings settings, ILogger<ManageDatsViewModel> log, IDatRepositoryService datRepository, IAceRepositoryService aceRepository, IKeywordRepositoryService keywordRepository, IDialogService dialogService) {
        _settings = settings;
        _log = log;
        _datRepository = datRepository;
        _aceRepository = aceRepository;
        _keywordRepository = keywordRepository;
        _dialogService = dialogService;

        _datRepository.SetRepositoryRoot(_settings.App.ManagedDatsDirectory);
        _aceRepository.SetRepositoryRoot(_settings.App.ManagedAceDbsDirectory);
        _keywordRepository.SetRepositoryRoot(_settings.App.ManagedKeywordsDirectory);

        _keywordRepository.GlobalProgress += OnKeywordGlobalProgress;

        RefreshList();
    }

    private void OnKeywordGlobalProgress(object? sender, IKeywordRepositoryService.KeywordGenerationProgress e) {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            if (IsLoading && LoadingStatus != null && (LoadingStatus.Contains("keyword", StringComparison.OrdinalIgnoreCase) || LoadingStatus.Contains("embedding", StringComparison.OrdinalIgnoreCase))) {
                LoadingStatus = e.Message;
                LoadingProgress = (e.KeywordProgress + e.NameEmbeddingProgress + e.DescEmbeddingProgress) / 3f * 100f;
            }
        });
    }

    public void Dispose() {
        _keywordRepository.GlobalProgress -= OnKeywordGlobalProgress;
    }

    private void RefreshList() {
        ManagedDataSets.Clear();
        foreach (var set in _datRepository.GetManagedDataSets()) {
            ManagedDataSets.Add(new ManagedDatSetViewModel(set, _datRepository, _log));
        }

        ManagedAceDbs.Clear();
        foreach (var db in _aceRepository.GetManagedAceDbs()) {
            ManagedAceDbs.Add(new ManagedAceDbViewModel(db, _aceRepository, _log));
        }

        ManagedKeywordDbs.Clear();
        foreach (var kw in _keywordRepository.GetManagedKeywordDbs()) {
            ManagedKeywordDbs.Add(new ManagedKeywordDbViewModel(kw, _datRepository, _aceRepository, _keywordRepository, _log));
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
    private async Task ImportAceDb() {
        var files = await TopLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions() {
            Title = "Choose ACE SQLite database to import",
            AllowMultiple = false,
            SuggestedStartLocation = await TopLevel.StorageProvider.TryGetFolderFromPathAsync(_settings.App.ProjectsDirectory),
            FileTypeFilter = new[] {
                new FilePickerFileType("SQLite Database") {
                    Patterns = new[] { "*.db", "*.sqlite" }
                }
            }
        });

        if (files.Count == 0) return;

        var localPath = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(localPath)) return;

        IsLoading = true;
        LoadingStatus = "Importing ACE DB...";
        LoadingProgress = 0f;

        var progress = new Progress<(string message, float progress)>(p => {
            LoadingStatus = p.message;
            LoadingProgress = p.progress * 100f;
        });

        var result = await _aceRepository.ImportAsync(localPath, null, progress, CancellationToken.None);
        
        IsLoading = false;

        if (result.IsSuccess) {
            RefreshList();
        }
        else {
            await _dialogService.ShowMessageBoxAsync(null, result.Error.Message, "Import Failed");
        }
    }

    [RelayCommand]
    private async Task DownloadAceDb() {
        IsLoading = true;
        LoadingStatus = "Fetching latest release...";
        LoadingProgress = 0f;

        var progress = new Progress<(string message, float progress)>(p => {
            LoadingStatus = p.message;
            LoadingProgress = p.progress * 100f;
        });

        var result = await _aceRepository.DownloadLatestAsync(progress, CancellationToken.None);
        
        IsLoading = false;

        if (result.IsSuccess) {
            RefreshList();
        }
        else {
            await _dialogService.ShowMessageBoxAsync(null, result.Error.Message, "Download Failed");
        }
    }

    [RelayCommand]
    private async Task RemoveAceDb(ManagedAceDbViewModel? dbVM) {
        if (dbVM == null) return;

        var confirm = await _dialogService.ShowMessageBoxAsync(null, 
            $"Are you sure you want to remove the ACE database '{dbVM.FriendlyName}'?\n\n" +
            "WARNING: Projects using this database will lose access to its data.",
            "Confirm Removal",
            MessageBoxButton.YesNo);

        if (confirm == true) {
            IsLoading = true;
            LoadingStatus = "Deleting file...";
            var result = await _aceRepository.DeleteAsync(dbVM.Id, CancellationToken.None);
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
    private async Task RegenerateKeywords(ManagedKeywordDbViewModel? kwVM) {
        if (kwVM == null) return;

        IsLoading = true;
        LoadingStatus = "Regenerating keywords...";
        LoadingProgress = 0f;

        var result = await _keywordRepository.GenerateAsync(kwVM.DatSetId, kwVM.AceDbId, true, CancellationToken.None);
        
        IsLoading = false;

        if (result.IsSuccess) {
            RefreshList();
        }
        else {
            await _dialogService.ShowMessageBoxAsync(null, result.Error.Message, "Regeneration Failed");
        }
    }

    [RelayCommand]
    private async Task RemoveKeywords(ManagedKeywordDbViewModel? kwVM) {
        if (kwVM == null) return;

        var confirm = await _dialogService.ShowMessageBoxAsync(null, 
            $"Are you sure you want to remove the keywords for '{kwVM.DatSetName}' / '{kwVM.AceDbName}'?",
            "Confirm Removal",
            MessageBoxButton.YesNo);

        if (confirm == true) {
            var result = await _keywordRepository.DeleteAsync(kwVM.DatSetId, kwVM.AceDbId, CancellationToken.None);
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
