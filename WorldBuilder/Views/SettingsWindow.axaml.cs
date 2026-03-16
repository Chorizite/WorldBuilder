using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using WorldBuilder.Lib.Input;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Services;
using WorldBuilder.ViewModels;
using WorldBuilder.Views.Settings;

namespace WorldBuilder.Views {
    public partial class SettingsWindow : Window {
        private WorldBuilderSettings? _originalSettings;
        private SettingsWindowViewModel? ViewModel => DataContext as SettingsWindowViewModel;
        private WorldBuilderSettings? Settings => ViewModel?.Settings;
        private SettingsUIGenerator? _uiGenerator;
        private ListBox? _navigationList;
        private InputManager? _inputManager;

        public SettingsWindow() {
            InitializeComponent();

            var settings = WorldBuilder.App.Services?.GetService<WorldBuilderSettings>();
            SetWindowPositionSize(settings);

            // Add window state event handlers
            SizeChanged += (s, e) => {
                Task.Delay(50).ContinueWith(_ => {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        if (settings?.Project != null && WindowState == WindowState.Normal) {
                            settings.Project.SettingsWindowWidth = Width;
                            settings.Project.SettingsWindowHeight = Height;
                        }
                    });
                });
            };

            PositionChanged += (s, e) => {
                Task.Delay(50).ContinueWith(_ => {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        if (settings?.Project != null && WindowState == WindowState.Normal) {
                            settings.Project.SettingsWindowX = Position.X;
                            settings.Project.SettingsWindowY = Position.Y;
                        }
                    });
                });
            };

            PropertyChanged += (s, e) => {
                if (settings?.Project != null && e.Property == WindowStateProperty) {
                    if (WindowState == WindowState.Normal || WindowState == WindowState.Maximized) {
                        settings.Project.SettingsWindowIsMaximized = WindowState == WindowState.Maximized;
                    }
                }
            };

            Closing += (s, e) => {
                if (settings?.Project != null)
                    settings.Project.Save();
            };
        }

        private void SetWindowPositionSize(WorldBuilderSettings? settings) {
            if (settings?.Project != null) {
                var unmaximizedWidth = settings.Project.SettingsWindowWidth;
                var unmaximizedHeight = settings.Project.SettingsWindowHeight;
                var unmaximizedX = settings.Project.SettingsWindowX;
                var unmaximizedY = settings.Project.SettingsWindowY;

                if (double.IsNaN(unmaximizedX) || double.IsNaN(unmaximizedY)) {
                    // Use relative positioning if no saved position exists
                    var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                    if (desktop?.MainWindow != null) {
                        var mainWindow = desktop.MainWindow;
                        var screen = mainWindow.Screens.ScreenFromWindow(mainWindow);
                        var scaling = screen?.Scaling ?? 1.0;

                        var offsetX = (int)(80 * scaling);
                        var offsetY = (int)(180 * scaling);

                        unmaximizedX = mainWindow.Position.X + offsetX;
                        unmaximizedY = mainWindow.Position.Y + offsetY;
                    }
                    else {
                        unmaximizedX = 100;
                        unmaximizedY = 100;
                    }
                }

                Width = unmaximizedWidth;
                Height = unmaximizedHeight;
                Position = new PixelPoint((int)unmaximizedX, (int)unmaximizedY);

                if (settings.Project.SettingsWindowIsMaximized) {
                    Loaded += (s, e) => {
                        WindowState = WindowState.Maximized;
                    };
                }
            }
        }

        protected override void OnDataContextChanged(EventArgs e) {
            base.OnDataContextChanged(e);

            if (DataContext is SettingsWindowViewModel viewModel && viewModel.Settings != null) {

                _inputManager = WorldBuilder.App.Services?.GetService<InputManager>()
                    ?? throw new InvalidOperationException("InputManager not available");

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
            
            // Add keymap to left menu navbar
            AddKeymapMenuItem(_navigationList);

            NavigationContainer.Children.Add(_navigationList);

            // Generate and add content panels
            var contentPanels = _uiGenerator.GenerateContentPanels();
            ContentContainer.Children.Add(contentPanels);

            // Add keymap panel
            var keymapScrollViewer = GenerateKeymapUI();
            ContentContainer.Children.Add(keymapScrollViewer);

            // Select first enabled item by default
            var firstEnabled = _navigationList.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.IsEnabled);
            if (firstEnabled != null) {
                _navigationList.SelectedItem = firstEnabled;
            }
        }

        private void AddKeymapMenuItem(ListBox navigationList) { 

            // Add keymap navigation item manually
            var applicationItem = navigationList.Items.OfType<ListBoxItem>()
                .FirstOrDefault(i => i.Content?.ToString() == "Application");

            if (applicationItem != null) {
                var keymapNavItem = new ListBoxItem {
                    Content = "Keymap",
                    Tag = "application-keymap",
                    Classes = { "NavSubSection" },
                    IsEnabled = true
                };
                var appIndex = navigationList.Items.IndexOf(applicationItem);
                navigationList.Items.Insert(appIndex + 1, keymapNavItem);
            }
        }

        private ScrollViewer GenerateKeymapUI() {
            var keymapScrollViewer = new ScrollViewer {
                Name = "applicationkeymapPanel",
                IsVisible = false,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            };
            keymapScrollViewer.Content = new KeymapPanelControl();
            return keymapScrollViewer;
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
                // Also handle direct ScrollViewer children
                if (child is ScrollViewer svb) {
                    svb.IsVisible = false;
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
                // Check for direct ScrollViewer children
                if (child is ScrollViewer svb && svb.Name == panelName) {
                    svb.IsVisible = true;
                    return;
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
                
                // Reload InputManager bindings from restored settings
                _inputManager?.LoadSettings();
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
                
                // Reload InputManager bindings from updated settings
                _inputManager?.LoadSettings();

                SetWindowPositionSize(new WorldBuilderSettings { Project = new ProjectSettings() });
            }
        }
    }
}