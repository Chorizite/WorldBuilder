using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Threading.Tasks;
using WorldBuilder.Lib;
using WorldBuilder.Lib.Extensions;
using WorldBuilder.Lib.Messages;
using static WorldBuilder.ViewModels.SplashPageViewModel;

namespace WorldBuilder.ViewModels;

public partial class CreateProjectViewModel : SplashPageViewModelBase, INotifyDataErrorInfo {
    private readonly Dictionary<string, List<string>> _errors = new();
    private readonly ILogger<CreateProjectViewModel> _log;
    private readonly WorldBuilderSettings _settings;

    [ObservableProperty]
    private string _baseDatDirectory = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectLocation))]
    private string _projectName = "New Project";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectLocation))]
    private string _location = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoNextCommand))]
    private bool _canProceed;

    [ObservableProperty]
    private List<string> _baseDatDirectoryErrors = new();

    [ObservableProperty]
    private List<string> _projectNameErrors = new();

    [ObservableProperty]
    private List<string> _locationErrors = new();

    public string ProjectLocation => Path.Combine(Location, ProjectName);

    public new event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
    
    // Add the HasErrors property (required by INotifyDataErrorInfo)
    public bool HasErrors => _errors.Any();

    // Add the GetErrors method (required by INotifyDataErrorInfo)
    public IEnumerable GetErrors(string? propertyName) {
        if (string.IsNullOrEmpty(propertyName))
            return _errors.Values.SelectMany(e => e);

        return _errors.TryGetValue(propertyName, out var errors) ? errors : Enumerable.Empty<string>();
    }

    public CreateProjectViewModel(WorldBuilderSettings settings, ILogger<CreateProjectViewModel> log) {
        _log = log;
        _settings = settings;

        _location = settings.ProjectsDirectory;
        ValidateLocation();
        UpdateCanProceed();

        // Subscribe to property changes to trigger validation and update CanProceed
        PropertyChanged += (s, e) => {
            switch (e.PropertyName) {
                case nameof(BaseDatDirectory):
                    ValidateBaseDatDirectory();
                    UpdateCanProceed();
                    break;
                case nameof(ProjectName):
                    ValidateProjectName();
                    ValidateLocation();
                    UpdateCanProceed();
                    break;
                case nameof(Location):
                    ValidateLocation();
                    UpdateCanProceed();
                    break;
            }
        };
    }

    [RelayCommand]
    private async Task BrowseBaseDatDirectory() {
        var files = await TopLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions() {
            Title = "Choose Base DAT directory",
            AllowMultiple = false,
            SuggestedStartLocation = await TopLevel.StorageProvider.TryGetFolderFromPathAsync(_settings.ProjectsDirectory)
        });

        if (files.Count == 0) return;

        var localPath = files[0].TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath)) {
            BaseDatDirectory = localPath;
        }
    }

    [RelayCommand]
    private async Task BrowseLocation() {
        var files = await TopLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions() {
            Title = "Choose project location",
            AllowMultiple = false,
            SuggestedStartLocation = await TopLevel.StorageProvider.TryGetFolderFromPathAsync(_settings.ProjectsDirectory)
        });

        if (files.Count == 0) return;

        var localPath = files[0].TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath)) {
            Location = localPath;
        }
    }

    [RelayCommand]
    private void GoBack() {
        WeakReferenceMessenger.Default.Send(new SplashPageChangedMessage(SplashPage.ProjectSelection));
    }

    [RelayCommand(CanExecute = nameof(CanProceed))]
    private void GoNext() {
        WeakReferenceMessenger.Default.Send(new CreateProjectMessage(this));
    }

    private void ValidateBaseDatDirectory() {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(BaseDatDirectory)) {
            errors.Add("Base DAT directory is required.");
        }
        else if (!Directory.Exists(BaseDatDirectory)) {
            errors.Add("Base DAT directory does not exist.");
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
                    errors.Add($"File '{path}' not found in the specified directory.");
                }
            }
        }

        BaseDatDirectoryErrors = errors;
        SetErrors(nameof(BaseDatDirectory), errors);
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
        else if (!Directory.Exists(Location)) {
            errors.Add("Location directory does not exist.");
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
        CanProceed = !HasErrors &&
                     !string.IsNullOrWhiteSpace(BaseDatDirectory) &&
                     !string.IsNullOrWhiteSpace(ProjectName) &&
                     !string.IsNullOrWhiteSpace(Location);
    }

    private void SetErrors(string propertyName, List<string> errors) {
        if (errors.Any())
            _errors[propertyName] = errors;
        else
            _errors.Remove(propertyName);

        foreach (var error in errors) {
            _log.LogError(error);
        }

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        OnPropertyChanged(nameof(HasErrors));
    }

}