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
        private bool _isValidating;

        [ObservableProperty]
        private string _exportDirectory = string.Empty;

        [ObservableProperty]
        private int _portalIteration = 1;

        [ObservableProperty]
        private int _cellIteration = 1;

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

        partial void OnCellIterationChanged(int value) {
            Validate();
        }

        partial void OnOverwriteFilesChanged(bool value) {
            Validate();
        }

        public ExportDatsWindowViewModel(WorldBuilderSettings settings, IDatReaderWriter dats, IDatExportService datExportService) {
            _settings = settings;
            _dats = dats;
            _datExportService = datExportService;

            ExportDirectory = !string.IsNullOrEmpty(_settings.Project?.Export.LastDatExportDirectory) ? _settings.Project.Export.LastDatExportDirectory : _settings.App.ProjectsDirectory;
            PortalIteration = (_settings.Project?.Export.LastDatExportPortalIteration ?? 0) > 0 ? _settings.Project!.Export.LastDatExportPortalIteration : _dats.PortalIteration;

            // Try to set the default cell iteration to the first cell region iteration if the setting is less than 1
            int defaultCellIteration = 1;
            foreach (var db in _dats.CellRegions.Values) {
                if (db.Iteration > 0) {
                    defaultCellIteration = db.Iteration;
                    break;
                }
            }
            CellIteration = (_settings.Project?.Export.LastDatExportCellIteration ?? 0) > 0 ? _settings.Project!.Export.LastDatExportCellIteration : defaultCellIteration;

            OverwriteFiles = _settings.Project?.Export.OverwriteDatFiles ?? true;

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

                var success = await _datExportService.ExportDatsAsync(ExportDirectory, PortalIteration, CellIteration, OverwriteFiles, progressHandler);
                if (success) {
                    if (_settings.Project is not null) {
                        _settings.Project.Export.LastDatExportDirectory = ExportDirectory;
                        _settings.Project.Export.LastDatExportPortalIteration = PortalIteration;
                        _settings.Project.Export.LastDatExportCellIteration = CellIteration;
                        _settings.Project.Export.OverwriteDatFiles = OverwriteFiles;
                    }
                    _settings.Save();

                    DialogResult = true;
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

                // Validate portal/cell iteration
                if (PortalIteration <= 0 || CellIteration <= 0) {
                    IterationErrorMessage = "Portal iteration and cell iteration must be greater than 0.";
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