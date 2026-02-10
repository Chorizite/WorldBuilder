using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanumanInstitute.MvvmDialogs;
using System;
using System.IO;
using System.Threading.Tasks;
using WorldBuilder.Services;

using WorldBuilder.Shared.Services;

namespace WorldBuilder.ViewModels {
    public partial class ExportDatsWindowViewModel : ViewModelBase, IModalDialogViewModel {
        private readonly WorldBuilderSettings _settings;
        private readonly IDatReaderWriter _dats;
        private readonly IDatExportService _datExportService;
        private bool _isValidating; // Reentrancy guard

        [ObservableProperty]
        private string _exportDirectory = string.Empty;

        [ObservableProperty]
        private int _portalIteration = 1;

        [ObservableProperty]
        private bool _overwriteFiles = false;

        [ObservableProperty]
        private bool _hasDirectoryError = false;

        [ObservableProperty]
        private string _directoryErrorMessage = string.Empty;

        [ObservableProperty]
        private bool _hasIterationError = false;

        [ObservableProperty]
        private string _iterationErrorMessage = string.Empty;

        [ObservableProperty]
        private bool _canExport = false;

        [ObservableProperty]
        private bool _isExporting = false;

        [ObservableProperty]
        private double _progress = 0;

        [ObservableProperty]
        private string _exportStatus = string.Empty;

        // Property for the dialog result
        public bool? DialogResult { get; set; }

        partial void OnExportDirectoryChanged(string value) {
            Validate();
        }

        partial void OnPortalIterationChanged(int value) {
            Validate();
        }

        partial void OnOverwriteFilesChanged(bool value) {
            Validate();
        }

        public ExportDatsWindowViewModel(WorldBuilderSettings settings, IDatReaderWriter dats, IDatExportService datExportService) {
            _settings = settings;
            _dats = dats;
            _datExportService = datExportService;

            ExportDirectory = _settings.App.ProjectsDirectory;
            PortalIteration = _dats.PortalIteration;

            Validate();
        }

        [RelayCommand]
        public async Task BrowseExportDirectory() {
            var folders = await TopLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions {
                Title = "Choose DAT export directory",
                AllowMultiple = false,
                SuggestedStartLocation = await TopLevel.StorageProvider.TryGetFolderFromPathAsync(ExportDirectory)
            });

            if (folders.Count > 0) {
                var localPath = folders[0].TryGetLocalPath();
                if (!string.IsNullOrWhiteSpace(localPath)) {
                    ExportDirectory = localPath;
                }
            }
        }

        public async Task<bool> Export() {
            if (!Validate()) return false;
            if (IsExporting) return false;

            IsExporting = true;
            CanExport = false;
            ExportStatus = "Starting export...";
            Progress = 0;

            try {
                var progressHandler = new Progress<DatExportProgress>(p => {
                    ExportStatus = p.Message;
                    Progress = p.Percent * 100;
                });

                var success = await _datExportService.ExportDatsAsync(ExportDirectory, PortalIteration, OverwriteFiles, progressHandler);
                if (success) {
                    DialogResult = true; // Set dialog result to true for success
                    return true;
                }
                else {
                    DirectoryErrorMessage = "Export failed. Check logs for details.";
                    HasDirectoryError = true;
                }
            }
            catch (Exception ex) {
                DirectoryErrorMessage = $"Export failed: {ex.Message}";
                HasDirectoryError = true;
            }
            finally {
                IsExporting = false;
                Validate();
            }

            return false;
        }

        private bool Validate() {
            if (_isValidating) return false; // Prevent reentrancy
            _isValidating = true;

            try {
                HasDirectoryError = false;
                HasIterationError = false;
                DirectoryErrorMessage = string.Empty;
                IterationErrorMessage = string.Empty;

                // Validate directory
                if (string.IsNullOrWhiteSpace(ExportDirectory)) {
                    DirectoryErrorMessage = "Export directory is required.";
                    HasDirectoryError = true;
                }
                else if (!Directory.Exists(ExportDirectory)) {
                    DirectoryErrorMessage = "Selected directory does not exist.";
                    HasDirectoryError = true;
                }

                // Validate portal iteration
                if (PortalIteration <= 0) {
                    IterationErrorMessage = "Portal iteration must be greater than 0.";
                    HasIterationError = true;
                }

                CanExport = !HasDirectoryError && !HasIterationError;
                return CanExport;
            }
            finally {
                _isValidating = false;
            }
        }
    }
}