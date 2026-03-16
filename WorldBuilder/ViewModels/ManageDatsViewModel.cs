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
/// View model for a single managed ACE DB in the management view.
/// </summary>
public partial class ManagedAceDbViewModel : ObservableObject {
    private readonly ManagedAceDb _model;
    private readonly IAceRepositoryService _aceRepository;
    private readonly ILogger _log;

    [ObservableProperty]
    private string _friendlyName;

    [ObservableProperty]
    private bool _isEditing;

    public Guid Id => _model.Id;
    public string BaseVersion => _model.BaseVersion;
    public string PatchVersion => _model.PatchVersion;
    public string LastModified => _model.LastModified;
    public string Md5 => _model.Md5;
    public DateTime ImportDate => _model.ImportDate;
    public string DisplayVersion => _model.DisplayVersion;

    public ManagedAceDbViewModel(ManagedAceDb model, IAceRepositoryService aceRepository, ILogger log) {
        _model = model;
        _aceRepository = aceRepository;
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

        var result = await _aceRepository.UpdateFriendlyNameAsync(Id, FriendlyName, CancellationToken.None);
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
/// View model for a single managed keyword database.
/// </summary>
public partial class ManagedKeywordDbViewModel : ObservableObject {
    private readonly ManagedKeywordDb _model;
    private readonly IDatRepositoryService _datRepository;
    private readonly IAceRepositoryService _aceRepository;
    private readonly IKeywordRepositoryService _keywordRepository;
    private readonly ILogger _log;

    public Guid DatSetId => _model.DatSetId;
    public Guid AceDbId => _model.AceDbId;
    public int GeneratorVersion => _model.GeneratorVersion;
    public DateTime LastGenerated => _model.LastGenerated;

    public string DatSetName => _datRepository.GetManagedDataSet(DatSetId)?.FriendlyName ?? DatSetId.ToString()[..8];
    public string AceDbName => _aceRepository.GetManagedAceDb(AceDbId)?.FriendlyName ?? AceDbId.ToString()[..8];

    public ManagedKeywordDbViewModel(ManagedKeywordDb model, IDatRepositoryService datRepository, IAceRepositoryService aceRepository, IKeywordRepositoryService keywordRepository, ILogger log) {
        _model = model;
        _datRepository = datRepository;
        _aceRepository = aceRepository;
        _keywordRepository = keywordRepository;
        _log = log;
    }
}

/// <summary>
/// View model for the manage DATs screen.
/// </summary>
public partial class ManageDatsViewModel : SplashPageViewModelBase {
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

        RefreshList();
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

        var progress = new Progress<(string message, float progress)>(p => {
            LoadingStatus = p.message;
            LoadingProgress = p.progress * 100f;
        });

        var result = await _keywordRepository.GenerateAsync(kwVM.DatSetId, kwVM.AceDbId, progress, CancellationToken.None);
        
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
