using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogHostAvalonia;
using System;
using System.IO;
using System.Threading.Tasks;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.ViewModels {
    public partial class ExportDatsWindowViewModel : ViewModelBase {
        private readonly WorldBuilderSettings _settings;
        private readonly Project _project;
        private readonly Window _window;
        private readonly string[] datFiles = new[]
        {
            "client_cell_1.dat",
            "client_portal.dat",
            "client_highres.dat",
            "client_local_English.dat"
        };
        private bool _isValidating; // Reentrancy guard

        [ObservableProperty]
        private string _exportDirectory = string.Empty;

        [ObservableProperty]
        private int _portalIteration = 0;

        [ObservableProperty]
        private int _currentPortalIteration = 0;

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

        partial void OnExportDirectoryChanged(string value) {
            Validate();
        }

        partial void OnPortalIterationChanged(int value) {
            Validate();
        }

        partial void OnOverwriteFilesChanged(bool value) {
            Validate();
        }

        public ExportDatsWindowViewModel(WorldBuilderSettings settings, Project project, Window window) {
            _settings = settings;
            _project = project;
            _window = window;

            ExportDirectory = _settings.App.ProjectsDirectory;
            CurrentPortalIteration = _project.DocumentManager.Dats.Dats.Portal.Iteration.CurrentIteration;
            PortalIteration = _project.DocumentManager.Dats.Dats.Portal.Iteration.CurrentIteration;

            Validate(); // Initial validation
        }

        [RelayCommand]
        public async Task BrowseExportDirectory() {
            var files = await _window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions {
                Title = "Choose DAT export directory",
                AllowMultiple = false,
                SuggestedStartLocation = await _window.StorageProvider.TryGetFolderFromPathAsync(_settings.App.ProjectsDirectory)
            });

            if (files.Count > 0) {
                var localPath = files[0].TryGetLocalPath();
                if (!string.IsNullOrWhiteSpace(localPath)) {
                    ExportDirectory = localPath; // This triggers OnExportDirectoryChanged
                }
            }
        }

        [RelayCommand]
        public async Task Export() {
            if (!Validate()) return;

            try {
                // Check if files exist and overwrite is not checked
                if (!OverwriteFiles) {
                    foreach (var datFile in datFiles) {
                        var filePath = Path.Combine(ExportDirectory, datFile);
                        if (File.Exists(filePath)) {
                            DirectoryErrorMessage = $"File {datFile} already exists. Check 'Overwrite existing DAT files' to replace.";
                            HasDirectoryError = true;
                            return;
                        }
                    }
                }

                await Task.Run(() => _project.ExportDats(ExportDirectory, PortalIteration));

                // Show success dialog using DialogHost
                await DialogHost.Show(new StackPanel {
                    Margin = new Avalonia.Thickness(10),
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = "DAT files exported successfully!" },
                        new Button
                        {
                            Content = "OK",
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            Command = new RelayCommand(() => DialogHost.Close("ExportDialogHost"))
                        }
                    }
                }, "ExportDialogHost");

                _window.Close();
            }
            catch (Exception ex) {
                DirectoryErrorMessage = $"Export failed: {ex.Message}";
                HasDirectoryError = true;

                // Show error dialog using DialogHost
                await DialogHost.Show(new StackPanel {
                    Margin = new Avalonia.Thickness(10),
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = $"Export failed: {ex.Message}" },
                        new Button
                        {
                            Content = "OK",
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            Command = new RelayCommand(() => DialogHost.Close("ExportDialogHost"))
                        }
                    }
                }, "ExportDialogHost");
            }
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