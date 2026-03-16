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
using WorldBuilder.Shared.Lib.Settings;
using WorldBuilder.Shared.Services;
using static WorldBuilder.ViewModels.SplashPageViewModel;

namespace WorldBuilder.ViewModels;

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
    /// Gets the ACE database selection view model.
    /// </summary>
    public AceDatabaseSelectionViewModel AceDatabaseSelection { get; }

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
    /// <param name="aceSelectionFactory">The ACE selection ViewModel factory</param>
    public CreateProjectViewModel(WorldBuilderSettings settings, ILogger<CreateProjectViewModel> log, IDatRepositoryService datRepository, IAceRepositoryService aceRepository, AceDatabaseSelectionViewModel aceSelectionViewModel) {
        _log = log;
        _settings = settings;
        _datRepository = datRepository;
        _aceRepository = aceRepository;
        AceDatabaseSelection = aceSelectionViewModel;

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

        ValidateSources();
        ValidateLocation();
        UpdateCanProceed();

        AceDatabaseSelection.PropertyChanged += (s, e) => {
            if (e.PropertyName == nameof(AceDatabaseSelection.SelectedAceSourceType) ||
                e.PropertyName == nameof(AceDatabaseSelection.SelectedManagedAceDb) ||
                e.PropertyName == nameof(AceDatabaseSelection.LocalAceDbPath)) {
                ValidateSources();
                UpdateCanProceed();
            }
        };

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

        _datRepository.SetRepositoryRoot(managedDatsDir);
        ManagedDataSets.Clear();
        foreach (var set in _datRepository.GetManagedDataSets()) {
            ManagedDataSets.Add(set);
        }

        if (previousDatId != null) {
            SelectedManagedDatSet = ManagedDataSets.FirstOrDefault(s => s.Id == previousDatId);
        }

        AceDatabaseSelection.UpdateManagedResources();
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

        if (AceDatabaseSelection.SelectedAceSourceType == AceSourceType.Local) {
            if (!string.IsNullOrWhiteSpace(AceDatabaseSelection.LocalAceDbPath) && !File.Exists(AceDatabaseSelection.LocalAceDbPath)) {
                aceErrors.Add("Local ACE database file does not exist.");
            }
        }

        BaseDatDirectoryErrors = datErrors;
        SetErrors(nameof(BaseDatDirectory), datErrors);

        AceDatabaseErrors = aceErrors;
        SetErrors(nameof(AceDatabaseSelection.SelectedManagedAceDb), aceErrors);
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

        var aceSourceValid = AceDatabaseSelection.SelectedAceSourceType == AceSourceType.Managed
            ? true // Optional
            : string.IsNullOrWhiteSpace(AceDatabaseSelection.LocalAceDbPath) || (!GetErrors(nameof(AceDatabaseSelection.SelectedManagedAceDb)).Cast<string>().Any());

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