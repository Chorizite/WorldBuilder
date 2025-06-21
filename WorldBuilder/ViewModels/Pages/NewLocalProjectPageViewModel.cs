using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Messaging;
using WorldBuilder.Factories;
using WorldBuilder.Messages;
using WorldBuilder.Shared.Models;
using System.Collections.Generic;
using System.Linq;
using WorldBuilder.Lib;
using WorldBuilder.Lib.Validation;

namespace WorldBuilder.ViewModels.Pages {
    public class NewLocalProjectPageViewModelValidator {
        public ValidationResult Validate(NewLocalProjectPageViewModel model) {
            var result = new ValidationResult();

            // Validate name
            if (string.IsNullOrWhiteSpace(model.Name) || model.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) {
                result.AddError(nameof(NewLocalProjectPageViewModel.Name), "Enter a valid project name.");
            }

            // Validate location
            if (string.IsNullOrWhiteSpace(model.Location) || !Directory.Exists(model.Location)) {
                result.AddError(nameof(NewLocalProjectPageViewModel.Location), "Select a valid project location.");
            }

            // Validate project doesn't already exist
            if (!string.IsNullOrWhiteSpace(model.Name) && !string.IsNullOrWhiteSpace(model.Location) &&
                Directory.Exists(Path.Combine(model.Location, model.Name))) {
                result.AddError(nameof(NewLocalProjectPageViewModel.Name), "A project with this name already exists at that location.");
            }

            // Validate base dat directory
            if (!ValidateBaseDatDirectory(model.BaseDatDirectory)) {
                result.AddError(nameof(NewLocalProjectPageViewModel.BaseDatDirectory), "Invalid base dat directory.");
            }

            result.IsValid = result.Errors.Count == 0;
            return result;
        }

        private bool ValidateBaseDatDirectory(string baseDatDirectory) {
            if (string.IsNullOrWhiteSpace(baseDatDirectory) || !Directory.Exists(baseDatDirectory)) {
                return false;
            }

            var requiredFiles = new[] {
                "client_cell_1.dat",
                "client_portal.dat",
                "client_highres.dat",
                "client_local_English.dat"
            };

            return requiredFiles.All(file => File.Exists(Path.Combine(baseDatDirectory, file)));
        }
    }

    public partial class NewLocalProjectPageViewModel : PageViewModel, INotifyDataErrorInfo {
        private readonly NewLocalProjectPageViewModelValidator _validator = new();
        private readonly Dictionary<string, List<string>> _errors = new();

        public override string WindowName => "New Local Project";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FullLocation))]
        private string _name = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FullLocation))]
        private string _location = string.Empty;

        [ObservableProperty]
        private string _baseDatDirectory = @"C:\Turbine\Asheron's Call\";

        [ObservableProperty]
        private ValidationResult _validationResult = new();

        public string FullLocation => Path.Combine(Location, Name);

        public bool HasErrors => _errors.Any(e => e.Value.Count > 0);

        public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

        public NewLocalProjectPageViewModel(WorldBuilderSettings settings) {
            Location = Path.GetFullPath(Path.Combine(settings.DataPath, "Projects"));
            UpdateValidation();
        }

        public IEnumerable GetErrors(string? propertyName) {
            return propertyName != null && _errors.TryGetValue(propertyName, out var errors) ? errors : Enumerable.Empty<string>();
        }

        partial void OnNameChanged(string value) => UpdateValidation();
        partial void OnLocationChanged(string value) => UpdateValidation();
        partial void OnBaseDatDirectoryChanged(string value) => UpdateValidation();

        private void UpdateValidation() {
            _errors.Clear();
            var result = _validator.Validate(this);
            var errorsByField = result.Errors.GroupBy(e => e.Field);

            foreach (var fieldGroup in errorsByField) {
                var fieldName = fieldGroup.Key;
                var fieldErrors = fieldGroup.Select(e => e.Message).ToList();
                _errors[fieldName] = fieldErrors;
            }

            ValidationResult = result;
            CreateProjectCommand.NotifyCanExecuteChanged();

            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(Name)));
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(Location)));
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(BaseDatDirectory)));
        }

        [RelayCommand]
        private void GoBack() {
            ParentWindow?.NavigateToPage(PageName.GettingStarted);
        }

        [RelayCommand]
        private async Task SelectLocation() {
            var topLevel = TopLevel.GetTopLevel(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null);

            if (topLevel == null || !topLevel.StorageProvider.CanPickFolder) return;

            var dir = string.IsNullOrWhiteSpace(Location) ? Path.GetDirectoryName(GetType().Assembly.Location) : Location;
            var res = await topLevel.StorageProvider.OpenFolderPickerAsync(new() {
                AllowMultiple = false,
                Title = "Select Project Location",
                SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(new Uri(dir))
            });

            if (res.Count == 1) {
                Location = res.First().TryGetLocalPath() ?? "";
            }
        }

        [RelayCommand]
        private async Task SelectBaseDatDirectory() {
            var topLevel = TopLevel.GetTopLevel(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null);

            if (topLevel == null || !topLevel.StorageProvider.CanPickFolder) return;

            var dir = string.IsNullOrWhiteSpace(BaseDatDirectory) ? Path.GetDirectoryName(GetType().Assembly.Location) : BaseDatDirectory;
            var res = await topLevel.StorageProvider.OpenFolderPickerAsync(new() {
                AllowMultiple = false,
                Title = "Select Base Dat Directory",
                SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(new Uri(dir))
            });

            if (res.Count == 1) {
                BaseDatDirectory = res.First().TryGetLocalPath() ?? "";
            }
        }

        [RelayCommand(CanExecute = nameof(CanCreateProject))]
        private Task CreateProject() {
            var project = Project.Create(Name, Path.Combine(Location, Name, $"{Name}.wbproj"), BaseDatDirectory);
            if (project == null) return Task.CompletedTask;
            WeakReferenceMessenger.Default.Send(new OpenProjectMessage(project));
            return Task.CompletedTask;
        }

        private bool CanCreateProject() => ValidationResult.IsValid;
    }

    public class NewLocalProjectPageViewModelDesign : NewLocalProjectPageViewModel {
        public NewLocalProjectPageViewModelDesign() : base(new()) {
            Location = Path.GetFullPath(Path.Combine((new WorldBuilderSettings()).DataPath, "Projects"));
        }
    }
}