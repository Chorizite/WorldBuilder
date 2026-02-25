using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldBuilder.Lib;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Services {
    public partial class WorldBuilderSettings : ObservableObject {
        private readonly ILogger<WorldBuilderSettings>? _log;

        [JsonIgnore]
        public string AppDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WorldBuilder");

        [JsonIgnore]
        public string SettingsFilePath => Path.Combine(AppDataDirectory, "settings.json");

        private AppSettings _app = new();
        public AppSettings App {
            get => _app;
            set {
                if (_app != null) _app.PropertyChanged -= OnSubSettingsPropertyChanged;
                if (SetProperty(ref _app, value) && _app != null) {
                    _app.PropertyChanged += OnSubSettingsPropertyChanged;
                }
            }
        }

        private LandscapeEditorSettings _landscape = new();
        public LandscapeEditorSettings Landscape {
            get => _landscape;
            set {
                if (_landscape != null) _landscape.PropertyChanged -= OnSubSettingsPropertyChanged;
                if (SetProperty(ref _landscape, value) && _landscape != null) {
                    _landscape.PropertyChanged += OnSubSettingsPropertyChanged;
                    LandscapeColorsSettings.Initialize(_landscape.Colors);
                }
            }
        }
        private DatBrowserSettings _datBrowser = new();
        public DatBrowserSettings DatBrowser {
            get => _datBrowser;
            set {
                if (_datBrowser != null) _datBrowser.PropertyChanged -= OnSubSettingsPropertyChanged;
                if (SetProperty(ref _datBrowser, value) && _datBrowser != null) {
                    _datBrowser.PropertyChanged += OnSubSettingsPropertyChanged;
                }
            }
        }

        private ProjectSettings? _project;
        [JsonIgnore]
        public ProjectSettings? Project {
            get => _project;
            set => SetProperty(ref _project, value);
        }

        public WorldBuilderSettings() {
            SetupListeners();
        }

        public WorldBuilderSettings(ILogger<WorldBuilderSettings> log) {
            _log = log;

            if (!Directory.Exists(AppDataDirectory)) {
                Directory.CreateDirectory(AppDataDirectory);
            }

            SetupListeners();
            TryLoad();
        }

        private void SetupListeners() {
            if (_app != null) _app.PropertyChanged += OnSubSettingsPropertyChanged;
            if (_landscape != null) {
                _landscape.PropertyChanged += OnSubSettingsPropertyChanged;
                LandscapeColorsSettings.Initialize(_landscape.Colors);
            }
            if (_datBrowser != null) {
                _datBrowser.PropertyChanged += OnSubSettingsPropertyChanged;
            }
        }

        private void OnSubSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (sender == _landscape && e.PropertyName == nameof(LandscapeEditorSettings.Colors)) {
                LandscapeColorsSettings.Initialize(_landscape.Colors);
            }
            Save();
        }

        private void TryLoad() {
            if (File.Exists(SettingsFilePath)) {
                try {
                    var json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<WorldBuilderSettings>(json, SourceGenerationContext.Default.WorldBuilderSettings);
                    if (settings != null) {
                        foreach (var property in typeof(WorldBuilderSettings).GetProperties()) {
                            if (property.CanWrite) {
                                property.SetValue(this, property.GetValue(settings));
                            }
                        }
                    }
                }
                catch (Exception ex) {
                    _log?.LogError(ex, "Failed to load settings");
                }
            }
        }

        public void Save() {
            var tmpFile = Path.GetTempFileName();
            try {
                var json = JsonSerializer.Serialize(this, SourceGenerationContext.Default.WorldBuilderSettings)
                    ?? throw new Exception("Failed to serialize settings to json");
                File.WriteAllText(tmpFile, json);
                File.Move(tmpFile, SettingsFilePath, true);
            }
            catch (Exception ex) {
                _log?.LogError(ex, "Failed to save settings");
            }
            finally {
                File.Delete(tmpFile);
            }
        }
    }
}