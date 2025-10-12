using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorldBuilder.Lib.Settings {
    public partial class WorldBuilderSettings : ObservableObject {
        private readonly ILogger<WorldBuilderSettings>? _log;

        [JsonIgnore]
        public string AppDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WorldBuilder");

        [JsonIgnore]
        public string SettingsFilePath => Path.Combine(AppDataDirectory, "settings.json");

        private AppSettings _app = new();
        public AppSettings App {
            get => _app;
            set => SetProperty(ref _app, value);
        }

        private LandscapeEditorSettings _landscape = new();
        public LandscapeEditorSettings Landscape {
            get => _landscape;
            set => SetProperty(ref _landscape, value);
        }

        public WorldBuilderSettings() { }

        public WorldBuilderSettings(ILogger<WorldBuilderSettings> log) {
            _log = log;

            if (!Directory.Exists(AppDataDirectory)) {
                Directory.CreateDirectory(AppDataDirectory);
            }

            TryLoad();
        }

        private void TryLoad() {
            if (File.Exists(SettingsFilePath)) {
                try {
                    var json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<WorldBuilderSettings>(json, SourceGenerationContext.Default.WorldBuilderSettings);
                    if (settings != null) {
                        foreach (var property in settings.GetType().GetProperties()) {
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
            catch(Exception ex) {
                _log?.LogError(ex, "Failed to save settings");
            }
            finally {
                File.Delete(tmpFile);
            }
        }
    }
}