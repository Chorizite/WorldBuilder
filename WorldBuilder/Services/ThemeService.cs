using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using WorldBuilder.Lib.Settings;

namespace WorldBuilder.Services {
    public partial class ThemeService : ObservableObject {
        private readonly WorldBuilderSettings _settings;

        public bool IsDarkMode => _settings.App.Theme == AppTheme.Dark || 
                                 (_settings.App.Theme == AppTheme.Default && Application.Current?.ActualThemeVariant == ThemeVariant.Dark);

        public ThemeService(WorldBuilderSettings settings) {
            _settings = settings;
            
            _settings.App.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(AppSettings.Theme)) {
                    OnPropertyChanged(nameof(IsDarkMode));
                }
            };

            if (Application.Current != null) {
                Application.Current.ActualThemeVariantChanged += (s, e) => {
                    if (_settings.App.Theme == AppTheme.Default) {
                        OnPropertyChanged(nameof(IsDarkMode));
                    }
                };
            }
        }

        public void ToggleTheme() {
            _settings.App.Theme = IsDarkMode ? AppTheme.Light : AppTheme.Dark;
        }
    }
}
