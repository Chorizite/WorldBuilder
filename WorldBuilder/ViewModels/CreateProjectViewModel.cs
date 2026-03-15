using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WorldBuilder.Lib;
using WorldBuilder.Messages;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;
using static WorldBuilder.ViewModels.SplashPageViewModel;

namespace WorldBuilder.ViewModels;

/// <summary>
/// The type of DAT source for a project.
/// </summary>
public enum DatSourceType {
    /// <summary>
    /// Use a managed DAT set.
    /// </summary>
    [Description("Managed")]
    Managed,
    /// <summary>
    /// Use a local DAT directory.
    /// </summary>
    [Description("Add New")]
    AddNew
}

/// <summary>
/// The type of ACE source for a project.
/// </summary>
public enum AceSourceType {
    /// <summary>
    /// Do not use an ACE database.
    /// </summary>
    [Description("None")]
    None,
    /// <summary>
    /// Use a managed ACE database.
    /// </summary>
    [Description("Managed")]
    Managed,
    /// <summary>
    /// Use a local ACE database file.
    /// </summary>
    [Description("Add New")]
    Local
}

/// <summary>
/// View model for the create project screen, handling project creation parameters and validation.
/// </summary>
public partial class CreateProjectViewModel : SplashPageViewModelBase, INotifyDataErrorInfo {
    private readonly Dictionary<string, List<string>> _errors = new();
    private readonly ILogger<CreateProjectViewModel> _log;
    private readonly WorldBuilderSettings _settings;
    private readonly IDatRepositoryService _datRepository;
    private readonly IAceRepositoryService _aceRepository;
    private string? _lastManagedResourcesDir;

    /// <summary>
    /// Gets or sets the loading overlay title.
    /// </summary>
    [ObservableProperty]
    private string _loadingTitle = "Creating Project...";

    /// <summary>
    /// Gets the available DAT source types.
    /// </summary>
    public List<DatSourceType> DatSourceTypes { get; } = [DatSourceType.Managed, DatSourceType.AddNew];

    /// <summary>
    /// Gets or sets the selected DAT source type.
    /// </summary>
    [ObservableProperty]
    private DatSourceType _selectedDatSourceType = DatSourceType.Managed;

    /// <summary>
    /// Gets or sets the base DAT directory path.
    /// </summary>
    [ObservableProperty]
    private string _baseDatDirectory = string.Empty;

    /// <summary>
    /// Gets or sets the selected existing managed DAT set.
    /// </summary>
    [ObservableProperty]
    private ManagedDatSet? _selectedManagedDatSet;

    /// <summary>
    /// Gets the collection of existing managed DAT sets.
    /// </summary>
    public ObservableCollection<ManagedDatSet> ManagedDataSets { get; } = [];

    /// <summary>
    /// Gets the available ACE source types.
    /// </summary>
    public List<AceSourceType> AceSourceTypes { get; } = [AceSourceType.None, AceSourceType.Managed, AceSourceType.Local];

    /// <summary>
    /// Gets or sets the selected ACE source type.
    /// </summary>
    [ObservableProperty]
    private AceSourceType _selectedAceSourceType = AceSourceType.Managed;

    /// <summary>
    /// Gets or sets the local ACE database path.
    /// </summary>
    [ObservableProperty]
    private string _localAceDbPath = string.Empty;

    /// <summary>
    /// Gets or sets the selected existing managed ACE DB.
    /// </summary>
    [ObservableProperty]
    private ManagedAceDb? _selectedManagedAceDb;

    /// <summary>
    /// Gets the collection of existing managed ACE DBs.
    /// </summary>
    public ObservableCollection<ManagedAceDb> ManagedAceDbs { get; } = [];

    /// <summary>
    /// Gets or sets the project name.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectLocation))]
    private string _projectName = "New Project";

    /// <summary>
    /// Gets or sets the project location directory.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectLocation))]
    private string _location = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the user can proceed with the next step.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoNextCommand))]
    private bool _canProceed;

    /// <summary>
    /// Gets or sets the errors related to the base DAT directory field.
    /// </summary>
    [ObservableProperty]
    private List<string> _baseDatDirectoryErrors = new();

    /// <summary>
    /// Gets or sets the errors related to the ACE database field.
    /// </summary>
    [ObservableProperty]
    private List<string> _aceDatabaseErrors = new();

    /// <summary>
    /// Gets or sets the errors related to the project name field.
    /// </summary>
    [ObservableProperty]
    private List<string> _projectNameErrors = new();

    /// <summary>
    /// Gets or sets the errors related to the location field.
    /// </summary>
    [ObservableProperty]
    private List<string> _locationErrors = new();

    /// <summary>
    /// Gets the full project location path.
    /// </summary>
    public string ProjectLocation => Path.Combine(Location, ProjectName);

    /// <summary>
    /// Occurs when the validation errors have changed for a property.
    /// </summary>
    public new event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    /// <summary>
    /// Gets a value indicating whether the view model has validation errors.
    /// </summary>
    // Add the HasErrors property (required by INotifyDataErrorInfo)
    public new bool HasErrors => _errors.Any();

    /// <summary>
    /// Gets the validation errors for a specified property or for the entire object.
    /// </summary>
    /// <param name="propertyName">The name of the property to retrieve errors for, or null to retrieve all errors</param>
    /// <returns>An enumerable collection of error strings</returns>
    // Add the GetErrors method (required by INotifyDataErrorInfo)
    public new IEnumerable GetErrors(string? propertyName) {
        if (string.IsNullOrEmpty(propertyName))
            return _errors.Values.SelectMany(e => e);

        return _errors.TryGetValue(propertyName, out var errors) ? errors : Enumerable.Empty<string>();
    }

    /// <summary>
    /// Initializes a new instance of the CreateProjectViewModel class.
    /// </summary>
    /// <param name="settings">The application settings</param>
    /// <param name="log">The logger instance</param>
    /// <param name="datRepository">The DAT repository service</param>
    /// <param name="aceRepository">The ACE repository service</param>
    public CreateProjectViewModel(WorldBuilderSettings settings, ILogger<CreateProjectViewModel> log, IDatRepositoryService datRepository, IAceRepositoryService aceRepository) {
        _log = log;
        _settings = settings;
        _datRepository = datRepository;
        _aceRepository = aceRepository;

        _location = settings.App.ProjectsDirectory;
        
        UpdateManagedResources();

        // Windows-specific Asheron's Call discovery
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            var defaultAcPath = @"C:\Turbine\Asheron's Call\";
            if (Directory.Exists(defaultAcPath) && Directory.EnumerateFiles(defaultAcPath, "client_*.dat").Any()) {
                _baseDatDirectory = defaultAcPath;
            }
        }

        if (string.IsNullOrEmpty(_baseDatDirectory)) {
            _baseDatDirectory = settings.App.LastBaseDatDirectory;
        }

        // Set default selection
        if (ManagedDataSets.Count > 0) {
            SelectedDatSourceType = DatSourceType.Managed;
            var endOfRetail = ManagedDataSets.FirstOrDefault(s => s.FriendlyName == "EndOfRetail");
            if (endOfRetail != null) {
                SelectedManagedDatSet = endOfRetail;
            }
            else {
                SelectedManagedDatSet = ManagedDataSets.First();
            }
        }
        else {
            SelectedDatSourceType = DatSourceType.AddNew;
        }

        if (ManagedAceDbs.Count > 0) {
            SelectedManagedAceDb = ManagedAceDbs.OrderByDescending(d => d.ImportDate).First();
        }

        ValidateSources();
        ValidateLocation();
        UpdateCanProceed();

        // Subscribe to property changes to trigger validation and update CanProceed
        PropertyChanged += (s, e) => {
            switch (e.PropertyName) {
                case nameof(SelectedDatSourceType):
                    ValidateSources();
                    UpdateCanProceed();
                    break;
                case nameof(BaseDatDirectory):
                    ValidateSources();
                    UpdateCanProceed();
                    break;
                case nameof(SelectedManagedDatSet):
                    ValidateSources();
                    UpdateCanProceed();
                    break;
                case nameof(SelectedAceSourceType):
                    ValidateSources();
                    UpdateCanProceed();
                    break;
                case nameof(LocalAceDbPath):
                    ValidateSources();
                    UpdateCanProceed();
                    break;
                case nameof(SelectedManagedAceDb):
                    ValidateSources();
                    UpdateCanProceed();
                    break;
                case nameof(ProjectName):
                    ValidateProjectName();
                    ValidateLocation();
                    UpdateManagedResources();
                    UpdateCanProceed();
                    break;
                case nameof(Location):
                    ValidateLocation();
                    UpdateManagedResources();
                    UpdateCanProceed();
                    break;
                case nameof(IsLoading):
                    UpdateCanProceed();
                    break;
            }
        };
    }

    private void UpdateManagedResources(bool force = false) {
        // Calculate ManagedResourcesDirectory relative to current Location
        var projectsRoot = Path.GetDirectoryName(Path.GetDirectoryName(ProjectLocation)) ?? string.Empty;
        var managedDatsDir = Path.Combine(projectsRoot, "Dats");
        var managedAceDbsDir = Path.Combine(projectsRoot, "Server");
        
        if (!force && projectsRoot == _lastManagedResourcesDir) return;
        _lastManagedResourcesDir = projectsRoot;

        var previousDatId = SelectedManagedDatSet?.Id;
        var previousAceId = SelectedManagedAceDb?.Id;

        _datRepository.SetRepositoryRoot(managedDatsDir);
        ManagedDataSets.Clear();
        foreach (var set in _datRepository.GetManagedDataSets()) {
            ManagedDataSets.Add(set);
        }

        if (previousDatId != null) {
            SelectedManagedDatSet = ManagedDataSets.FirstOrDefault(s => s.Id == previousDatId);
        }

        _aceRepository.SetRepositoryRoot(managedAceDbsDir);
        ManagedAceDbs.Clear();
        foreach (var db in _aceRepository.GetManagedAceDbs()) {
            ManagedAceDbs.Add(db);
        }

        if (previousAceId != null) {
            SelectedManagedAceDb = ManagedAceDbs.FirstOrDefault(d => d.Id == previousAceId);
        }
    }

    /// <summary>
    /// Downloads the latest ACE DB from GitHub.
    /// </summary>
    [RelayCommand]
    private async Task DownloadLatestAceDb() {
        LoadingTitle = "Downloading ACE Database...";
        IsLoading = true;
        LoadingStatus = "Preparing download...";
        LoadingProgress = 0f;

        var progress = new Progress<(string message, float progress)>(p => {
            LoadingStatus = p.message;
            LoadingProgress = p.progress * 100f;
        });

        var result = await _aceRepository.DownloadLatestAsync(progress, default);
        
        IsLoading = false;
        LoadingTitle = "Creating Project..."; // Reset for project creation

        if (result.IsSuccess) {
            // Re-scan repository to find the new database
            UpdateManagedResources(true);
            SelectedAceSourceType = AceSourceType.Managed;
            SelectedManagedAceDb = ManagedAceDbs.FirstOrDefault(d => d.Id == result.Value.Id);
        }
        else {
            _log.LogError("Failed to download ACE DB: {Error}", result.Error.Message);
        }
    }

    /// <summary>
    /// Opens a folder picker to select the base DAT directory.
    /// </summary>
    /// <returns>A task representing the asynchronous operation</returns>
    [RelayCommand]
    private async Task BrowseBaseDatDirectory() {
        var suggestedPath = string.IsNullOrEmpty(_settings.App.LastBaseDatDirectory) ? _settings.App.ProjectsDirectory : _settings.App.LastBaseDatDirectory;
        var files = await TopLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions() {
            Title = "Choose Base DAT directory",
            AllowMultiple = false,
            SuggestedStartLocation = await TopLevel.StorageProvider.TryGetFolderFromPathAsync(suggestedPath)
        });

        if (files.Count == 0) return;

        var localPath = files[0].TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath)) {
            BaseDatDirectory = localPath;
        }
    }

    /// <summary>
    /// Opens a file picker to select a local ACE database.
    /// </summary>
    /// <returns>A task representing the asynchronous operation</returns>
    [RelayCommand]
    private async Task BrowseLocalAceDb() {
        var files = await TopLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions() {
            Title = "Choose ACE SQLite database",
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
        if (!string.IsNullOrWhiteSpace(localPath)) {
            LocalAceDbPath = localPath;
        }
    }

    /// <summary>
    /// Opens a folder picker to select the project location.
    /// </summary>
    /// <returns>A task representing the asynchronous operation</returns>
    [RelayCommand]
    private async Task BrowseLocation() {
        var files = await TopLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions() {
            Title = "Choose project location",
            AllowMultiple = false,
            SuggestedStartLocation = await TopLevel.StorageProvider.TryGetFolderFromPathAsync(_settings.App.ProjectsDirectory)
        });

        if (files.Count == 0) return;

        var localPath = files[0].TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath)) {
            Location = localPath;
        }
    }

    /// <summary>
    /// Navigates back to the project selection screen.
    /// </summary>
    [RelayCommand]
    private void GoBack() {
        WeakReferenceMessenger.Default.Send(new SplashPageChangedMessage(SplashPage.ProjectSelection));
    }

    /// <summary>
    /// Proceeds to create the project with the specified parameters.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanProceed))]
    private void GoNext() {
        WeakReferenceMessenger.Default.Send(new CreateProjectMessage(this));
    }

    private void ValidateSources() {
        var datErrors = new List<string>();
        var aceErrors = new List<string>();

        if (SelectedDatSourceType == DatSourceType.Managed) {
            if (SelectedManagedDatSet == null) {
                datErrors.Add("A managed DAT set must be selected.");
            }
        }
        else {
            if (string.IsNullOrWhiteSpace(BaseDatDirectory)) {
                datErrors.Add("Base DAT directory is required.");
            }
            else if (!Directory.Exists(BaseDatDirectory)) {
                datErrors.Add("Base DAT directory does not exist.");
            }
            else {
                var paths = new[] {
                    "client_cell_1.dat",
                    "client_portal.dat",
                    "client_highres.dat",
                    "client_local_English.dat"
                };

                foreach (var path in paths) {
                    var filePath = Path.Combine(BaseDatDirectory, path);
                    if (!File.Exists(filePath)) {
                        datErrors.Add($"File '{path}' not found in the specified directory.");
                    }
                }
            }
        }

        if (SelectedAceSourceType == AceSourceType.Local) {
            if (!string.IsNullOrWhiteSpace(LocalAceDbPath) && !File.Exists(LocalAceDbPath)) {
                aceErrors.Add("Local ACE database file does not exist.");
            }
        }

        BaseDatDirectoryErrors = datErrors;
        SetErrors(nameof(BaseDatDirectory), datErrors);

        AceDatabaseErrors = aceErrors;
        SetErrors(nameof(SelectedManagedAceDb), aceErrors);
    }

    private void ValidateProjectName() {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(ProjectName)) {
            errors.Add("Project name is required.");
        }
        else {
            // Check for invalid file/directory characters
            var invalidChars = Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).ToArray();
            if (ProjectName.IndexOfAny(invalidChars) >= 0)
                errors.Add("Project name contains invalid characters.");

            // Additional checks for reserved names and other restrictions
            var reservedNames = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
            if (reservedNames.Contains(ProjectName.ToUpperInvariant()))
                errors.Add("Project name cannot be a reserved system name.");

            if (ProjectName.EndsWith(".") || ProjectName.EndsWith(" "))
                errors.Add("Project name cannot end with a period or space.");
        }

        ProjectNameErrors = errors;
        SetErrors(nameof(ProjectName), errors);
    }

    private void ValidateLocation() {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Location)) {
            errors.Add("Location is required.");
        }
        else if (!string.IsNullOrWhiteSpace(ProjectName)) {
            var projectPath = Path.Combine(Location, ProjectName);
            if (Directory.Exists(projectPath))
                errors.Add($"A directory named '{ProjectName}' already exists in the specified location.");
        }

        LocationErrors = errors;
        SetErrors(nameof(Location), errors);
    }

    private void UpdateCanProceed() {
        var datSourceValid = SelectedDatSourceType == DatSourceType.Managed
            ? SelectedManagedDatSet != null
            : !string.IsNullOrWhiteSpace(BaseDatDirectory) && !GetErrors(nameof(BaseDatDirectory)).Cast<string>().Any();

        var aceSourceValid = SelectedAceSourceType == AceSourceType.Managed
            ? true // Optional
            : string.IsNullOrWhiteSpace(LocalAceDbPath) || (!GetErrors(nameof(SelectedManagedAceDb)).Cast<string>().Any());

        CanProceed = !HasErrors &&
                     !IsLoading &&
                     datSourceValid &&
                     aceSourceValid &&
                     !string.IsNullOrWhiteSpace(ProjectName) &&
                     !string.IsNullOrWhiteSpace(Location);
    }

    private void SetErrors(string propertyName, List<string> errors) {
        if (errors.Any())
            _errors[propertyName] = errors;
        else
            _errors.Remove(propertyName);


        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        OnPropertyChanged(nameof(HasErrors));
    }

}