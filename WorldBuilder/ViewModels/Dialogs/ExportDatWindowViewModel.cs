using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using WorldBuilder.Lib.Validation;
using WorldBuilder.Shared.Models;
using WorldBuilder.ViewModels.Pages;

namespace WorldBuilder.ViewModels.Dialogs {
    public class ExportDatWindowViewModelValidator {
        public ValidationResult Validate(ExportDatWindowViewModel model) {
            var result = new ValidationResult();

            var datFiles = new[] {
                "client_cell_1.dat",
                "client_portal.dat",
                "client_highres.dat",
                "client_local_English.dat"
            };

            if (!Directory.Exists(model.ExportDirectory)) {
                result.AddError(nameof(ExportDatWindowViewModel.ExportDirectory), "Export directory does not exist.");
            }

            if (!model.OverwriteFiles && datFiles.Any(file => File.Exists(Path.Combine(model.ExportDirectory, file)))) {
                result.AddError(nameof(ExportDatWindowViewModel.ExportDirectory), "Export directory already contains dat files.");
            }

            if (model.CellIteration <= 0) {
                result.AddError(nameof(ExportDatWindowViewModel.CellIteration), "Enter a valid cell iteration. Must be greater than 0.");
            }

            if (model.PortalIteration <= 0) {
                result.AddError(nameof(ExportDatWindowViewModel.PortalIteration), "Enter a valid portal iteration. Must be greater than 0.");
            }

            if (model.LanguageIteration <= 0) {
                result.AddError(nameof(ExportDatWindowViewModel.LanguageIteration), "Enter a valid language iteration. Must be greater than 0.");
            }

            if (model.HighResIteration <= 0) {
                result.AddError(nameof(ExportDatWindowViewModel.HighResIteration), "Enter a valid high res iteration. Must be greater than 0.");
            }

            result.IsValid = result.Errors.Count == 0;
            return result;
        }
    }

    public partial class ExportDatWindowViewModel : BaseViewModel, INotifyDataErrorInfo {
        private readonly ExportDatWindowViewModelValidator _validator = new();
        private readonly Dictionary<string, List<string>> _errors = new();
        
        public Window ParentWindow { get; set; }

        [ObservableProperty]
        private Project _project;

        [ObservableProperty]
        private string _exportDirectory = String.Empty;

        [ObservableProperty]
        private int _cellIteration = 0;

        [ObservableProperty]
        private int _portalIteration = 0;

        [ObservableProperty]
        private int _languageIteration = 0;

        [ObservableProperty]
        private int _highResIteration = 0;

        [ObservableProperty]
        private bool _overwriteFiles = false;

        [ObservableProperty]
        private ValidationResult _validationResult = new();

        public bool HasErrors => _errors.Any(e => e.Value.Count > 0);

        public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

        public ExportDatWindowViewModel(Project project) {
            Project = project;
            CellIteration = Project.Dats.Cell.Iteration.CurrentIteration;
            PortalIteration = Project.Dats.Portal.Iteration.CurrentIteration;
            LanguageIteration = Project.Dats.Local.Iteration.CurrentIteration;
            HighResIteration = Project.Dats.HighRes.Iteration.CurrentIteration;
        }

        public IEnumerable GetErrors(string? propertyName) {
            return propertyName != null && _errors.TryGetValue(propertyName, out var errors) ? errors : Enumerable.Empty<string>();
        }

        partial void OnExportDirectoryChanged(string value) => UpdateValidation();
        partial void OnCellIterationChanged(int value) => UpdateValidation();
        partial void OnPortalIterationChanged(int value) => UpdateValidation();
        partial void OnLanguageIterationChanged(int value) => UpdateValidation();
        partial void OnHighResIterationChanged(int value) => UpdateValidation();
        partial void OnOverwriteFilesChanged(bool value) => UpdateValidation();


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
            ExportDatsCommand.NotifyCanExecuteChanged();

            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(ExportDirectory)));
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(CellIteration)));
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(PortalIteration)));
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(LanguageIteration)));
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(HighResIteration)));
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(OverwriteFiles)));
        }

        [RelayCommand]
        private async Task SelectExportDirectory() {
            var topLevel = TopLevel.GetTopLevel(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null);

            if (topLevel == null || topLevel.StorageProvider.CanPickFolder == false) return;
            var dir = string.IsNullOrWhiteSpace(ExportDirectory) ? Path.GetDirectoryName(GetType().Assembly.Location) : ExportDirectory;
            var res = await topLevel.StorageProvider.OpenFolderPickerAsync(new() {
                AllowMultiple = false,
                Title = "Select Export Directory",
                SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(new Uri(dir))
            });

            if (res.Count == 1) {
                ExportDirectory = res.First().TryGetLocalPath() ?? "";
            }
        }

        [RelayCommand]
        private void GoBack() => ParentWindow?.Close();

        [RelayCommand(CanExecute = nameof(CanExportDats))]
        private void ExportDats() {
            Project.ExportDats(ExportDirectory, CellIteration, PortalIteration, LanguageIteration, HighResIteration);
            GoBack();
        }

        private bool CanExportDats() => ValidationResult.IsValid;
    }
}
