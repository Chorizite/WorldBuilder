using System;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace WorldBuilder.Lib.Settings {
    [SettingCategory("Project", Order = -1)]
    public partial class ProjectSettings : ObservableObject {
        [SettingHidden]
        private double _windowWidth = 1280;
        public double WindowWidth { get => _windowWidth; set => SetProperty(ref _windowWidth, value); }

        [SettingHidden]
        private double _windowHeight = 720;
        public double WindowHeight { get => _windowHeight; set => SetProperty(ref _windowHeight, value); }

        [SettingHidden]
        private double _windowX = double.NaN;
        public double WindowX { get => _windowX; set => SetProperty(ref _windowX, value); }

        [SettingHidden]
        private double _windowY = double.NaN;
        public double WindowY { get => _windowY; set => SetProperty(ref _windowY, value); }

        [SettingHidden]
        private bool _isMaximized = false;
        public bool IsMaximized { get => _isMaximized; set => SetProperty(ref _isMaximized, value); }

        private CancellationTokenSource? _saveCts;

        public ProjectSettings() {
            PropertyChanged += (s, e) => RequestSave();
        }

        [SettingHidden]
        private Dictionary<string, bool> _layerVisibility = new();
        public Dictionary<string, bool> LayerVisibility { get => _layerVisibility; set => SetProperty(ref _layerVisibility, value); }

        [SettingHidden]
        private Dictionary<string, bool> _layerExpanded = new();
        public Dictionary<string, bool> LayerExpanded { get => _layerExpanded; set => SetProperty(ref _layerExpanded, value); }

        [SettingHidden]
        private Vector3 _landscapeCameraPosition = new Vector3(-701.20f, -5347.16f, 2000f);
        public Vector3 LandscapeCameraPosition { get => _landscapeCameraPosition; set => SetProperty(ref _landscapeCameraPosition, value); }

        [SettingHidden]
        private float _landscapeCameraYaw = 0;
        public float LandscapeCameraYaw { get => _landscapeCameraYaw; set => SetProperty(ref _landscapeCameraYaw, value); }

        [SettingHidden]
        private float _landscapeCameraPitch = -89.9f;
        public float LandscapeCameraPitch { get => _landscapeCameraPitch; set => SetProperty(ref _landscapeCameraPitch, value); }

        [SettingHidden]
        private bool _landscapeCameraIs3D = true;
        public bool LandscapeCameraIs3D { get => _landscapeCameraIs3D; set => SetProperty(ref _landscapeCameraIs3D, value); }

        [SettingHidden]
        private float _landscapeCameraZoom = 1.0f;
        public float LandscapeCameraZoom { get => _landscapeCameraZoom; set => SetProperty(ref _landscapeCameraZoom, value); }

        [SettingDisplayName("Overwrite DAT Files")]
        [SettingDescription("Whether to overwrite existing DAT files when exporting.")]
        private bool _overwriteDatFiles = true;
        public bool OverwriteDatFiles { get => _overwriteDatFiles; set => SetProperty(ref _overwriteDatFiles, value); }

        [SettingDescription("Last directory used for DAT export")]
        [SettingPath(PathType.Folder, DialogTitle = "Select Last DAT Export Directory")]
        private string _lastDatExportDirectory = string.Empty;
        public string LastDatExportDirectory { get => _lastDatExportDirectory; set => SetProperty(ref _lastDatExportDirectory, value); }

        [SettingDescription("Last portal iteration used for DAT export")]
        private int _lastDatExportPortalIteration = 0;
        public int LastDatExportPortalIteration { get => _lastDatExportPortalIteration; set => SetProperty(ref _lastDatExportPortalIteration, value); }

        [JsonIgnore]
        [SettingHidden]
        public string? FilePath { get; set; }

        public void Save() {
            if (string.IsNullOrEmpty(FilePath)) return;

            _saveCts?.Cancel();
            _saveCts?.Dispose();
            _saveCts = null;

            var json = System.Text.Json.JsonSerializer.Serialize(this, SourceGenerationContext.Default.ProjectSettings);
            System.IO.File.WriteAllText(FilePath, json);
        }

        public void RequestSave() {
            if (string.IsNullOrEmpty(FilePath)) return;

            _saveCts?.Cancel();
            _saveCts?.Dispose();
            _saveCts = new CancellationTokenSource();

            var token = _saveCts.Token;
            Task.Run(async () => {
                try {
                    await Task.Delay(2000, token);
                    Save();
                }
                catch (OperationCanceledException) { }
            }, token);
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