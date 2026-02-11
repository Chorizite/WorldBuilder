using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WorldBuilder.Lib.Settings {
    [SettingCategory("Project", Order = -1)]
    public partial class ProjectSettings : ObservableObject {
        [SettingHidden]
        private double _windowWidth = 1280;
        public double WindowWidth { get => _windowWidth; set => SetProperty(ref _windowWidth, value); }

        [SettingHidden]
        private double _windowHeight = 720;
        public double WindowHeight { get => _windowHeight; set => SetProperty(ref _windowHeight, value); }

        private Dictionary<string, bool> _layerVisibility = new();
        public Dictionary<string, bool> LayerVisibility { get => _layerVisibility; set => SetProperty(ref _layerVisibility, value); }

        private Dictionary<string, bool> _layerExpanded = new();
        public Dictionary<string, bool> LayerExpanded { get => _layerExpanded; set => SetProperty(ref _layerExpanded, value); }

        [JsonIgnore]
        public string? FilePath { get; set; }

        public void Save() {
            if (string.IsNullOrEmpty(FilePath)) return;

            var json = System.Text.Json.JsonSerializer.Serialize(this, SourceGenerationContext.Default.ProjectSettings);
            System.IO.File.WriteAllText(FilePath, json);
        }

        public static ProjectSettings Load(string filePath) {
            if (!System.IO.File.Exists(filePath)) {
                return new ProjectSettings { FilePath = filePath };
            }

            try {
                var json = System.IO.File.ReadAllText(filePath);
                var settings = System.Text.Json.JsonSerializer.Deserialize(json, SourceGenerationContext.Default.ProjectSettings);
                if (settings != null) {
                    settings.FilePath = filePath;
                    return settings;
                }
            }
            catch {
                // Fallback to defaults
            }

            return new ProjectSettings { FilePath = filePath };
        }
    }
}