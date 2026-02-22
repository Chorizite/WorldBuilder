using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Services;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Views {
    public partial class SettingsWindow : Window {
        private WorldBuilderSettings? _originalSettings;
        private SettingsWindowViewModel? ViewModel => DataContext as SettingsWindowViewModel;
        private WorldBuilderSettings? Settings => ViewModel?.Settings;
        private SettingsUIGenerator? _uiGenerator;
        private ListBox? _navigationList;

        public SettingsWindow() {
            InitializeComponent();
        }

        protected override void OnDataContextChanged(EventArgs e) {
            base.OnDataContextChanged(e);

            if (DataContext is SettingsWindowViewModel viewModel && viewModel.Settings != null) {
                // Store a copy of original settings for cancel/reset
                _originalSettings = SettingsCloner.Clone(viewModel.Settings);

                // Generate UI dynamically
                GenerateUI(viewModel.Settings);
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

            // Select first enabled item by default
            var firstEnabled = _navigationList.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.IsEnabled);
            if (firstEnabled != null) {
                _navigationList.SelectedItem = firstEnabled;
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
                if (Settings != null) {
                    Settings.App.ProjectsDirectory = folders[0].Path.LocalPath;
                }
            }
        }

        private void Save_Click(object? sender, RoutedEventArgs e) {
            if (Settings != null) {
                Settings.Save();
            }

            Close();
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e) {
            // Restore original settings
            if (_originalSettings != null && Settings != null) {
                SettingsCloner.Restore(_originalSettings, Settings);
            }

            Close();
        }

        protected override void OnClosed(EventArgs e) {
            base.OnClosed(e);
            ViewModel?.OnClosed();
        }

        private void ResetToDefaults_Click(object? sender, RoutedEventArgs e) {
            // Reset to default values
            if (Settings != null) {
                SettingsCloner.ResetToDefaults(Settings, _originalSettings?.Project);
            }
        }
    }
}