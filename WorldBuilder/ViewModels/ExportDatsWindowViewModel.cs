using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanumanInstitute.MvvmDialogs;
using System;
using System.IO;
using System.Threading.Tasks;
using WorldBuilder.Services;

namespace WorldBuilder.ViewModels 
{
    public partial class ExportDatsWindowViewModel : ViewModelBase, IModalDialogViewModel
    {
        private readonly WorldBuilderSettings _settings;
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

        public ExportDatsWindowViewModel(WorldBuilderSettings settings) {
            _settings = settings;

            ExportDirectory = _settings.App.ProjectsDirectory;

            Validate();
        }

        [RelayCommand]
        public async Task BrowseExportDirectory() {
            // This will now be handled by the code-behind
        }

        public async Task<bool> Export() {
            if (!Validate()) return false;

            try {
                // TODO: Implement actual export functionality
                DialogResult = true; // Set dialog result to true for success
                return true;
            }
            catch (Exception ex) {
                DirectoryErrorMessage = $"Export failed: {ex.Message}";
                HasDirectoryError = true;
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