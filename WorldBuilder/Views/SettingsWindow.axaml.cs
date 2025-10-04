using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.Linq;
using WorldBuilder.Lib.Settings;

namespace WorldBuilder.Views {
    public partial class SettingsWindow : Window {
        private WorldBuilderSettings? _originalSettings;
        private WorldBuilderSettings Settings => (WorldBuilderSettings)DataContext!;
        private SettingsUIGenerator? _uiGenerator;
        private ListBox? _navigationList;

        public SettingsWindow() {
            InitializeComponent();
        }

        protected override void OnDataContextChanged(EventArgs e) {
            base.OnDataContextChanged(e);

            if (DataContext is WorldBuilderSettings settings) {
                // Store a copy of original settings for cancel/reset
                _originalSettings = SettingsCloner.Clone(settings);

                // Generate UI dynamically
                GenerateUI(settings);
            }
        }

        private void GenerateUI(WorldBuilderSettings settings) {
            _uiGenerator = new SettingsUIGenerator(settings, this);

            // Generate and add navigation
            _navigationList = _uiGenerator.GenerateNavigation();
            _navigationList.SelectionChanged += Navigation_SelectionChanged;
            NavigationContainer.Children.Add(_navigationList);

            // Generate and add content panels
            var contentPanels = _uiGenerator.GenerateContentPanels();
            ContentContainer.Children.Add(contentPanels);

            // Select first item by default
            if (_navigationList.Items.Count > 0) {
                _navigationList.SelectedIndex = 0;
            }
        }

        private void Navigation_SelectionChanged(object? sender, SelectionChangedEventArgs e) {
            if (_navigationList?.SelectedItem is not ListBoxItem item || item.Tag is not string tag) {
                return;
            }

            // Hide all panels
            foreach (var child in ContentContainer.Children) {
                if (child is Panel panel) {
                    foreach (var innerChild in panel.Children) {
                        if (innerChild is ScrollViewer sv) {
                            sv.IsVisible = false;
                        }
                    }
                }
            }

            // Show selected panel
            var panelName = tag.Replace("-", "") + "Panel";
            foreach (var child in ContentContainer.Children) {
                if (child is Panel panel) {
                    foreach (var innerChild in panel.Children) {
                        if (innerChild is ScrollViewer sv && sv.Name == panelName) {
                            sv.IsVisible = true;
                            return;
                        }
                    }
                }
            }
        }

        private async void BrowseProjectsDirectory_Click(object? sender, RoutedEventArgs e) {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions {
                Title = "Select Projects Directory",
                AllowMultiple = false
            });

            if (folders.Count > 0) {
                Settings.App.ProjectsDirectory = folders[0].Path.LocalPath;
            }
        }

        private void Save_Click(object? sender, RoutedEventArgs e) {
            Settings.Save();
            Close();
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e) {
            // Restore original settings
            if (_originalSettings != null) {
                SettingsCloner.Restore(_originalSettings, Settings);
            }
            Close();
        }

        private void ResetToDefaults_Click(object? sender, RoutedEventArgs e) {
            // Reset to default values
            SettingsCloner.ResetToDefaults(Settings, () => new WorldBuilderSettings(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<WorldBuilderSettings>.Instance
            ));
        }
    }
}