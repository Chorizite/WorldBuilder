using System;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using WorldBuilder.Shared.Lib.Settings;

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

        [SettingHidden]
        private ProjectGraphicsSettings _graphics = new();
        public ProjectGraphicsSettings Graphics {
            get => _graphics;
            set {
                if (_graphics != null) _graphics.PropertyChanged -= OnSubSettingsPropertyChanged;
                if (SetProperty(ref _graphics, value) && _graphics != null) {
                    _graphics.PropertyChanged += OnSubSettingsPropertyChanged;
                }
            }
        }

        [SettingHidden]
        private ProjectExportSettings _export = new();
        public ProjectExportSettings Export {
            get => _export;
            set {
                if (_export != null) _export.PropertyChanged -= OnSubSettingsPropertyChanged;
                if (SetProperty(ref _export, value) && _export != null) {
                    _export.PropertyChanged += OnSubSettingsPropertyChanged;
                }
            }
        }

        [SettingHidden]
        private LandscapeToolsSettings _landscapeTools = new();
        public LandscapeToolsSettings LandscapeTools {
            get => _landscapeTools;
            set {
                if (_landscapeTools != null) _landscapeTools.PropertyChanged -= OnSubSettingsPropertyChanged;
                if (SetProperty(ref _landscapeTools, value) && _landscapeTools != null) {
                    _landscapeTools.PropertyChanged += OnSubSettingsPropertyChanged;
                }
            }
        }

        private void OnSubSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
            RequestSave();
        }

        private CancellationTokenSource? _saveCts;

        public ProjectSettings() {
            PropertyChanged += (s, e) => RequestSave();

            if (_graphics != null) _graphics.PropertyChanged += OnSubSettingsPropertyChanged;
            if (_export != null) _export.PropertyChanged += OnSubSettingsPropertyChanged;
            if (_landscapeTools != null) _landscapeTools.PropertyChanged += OnSubSettingsPropertyChanged;
        }

        [SettingHidden]
        private Dictionary<string, bool> _layerVisibility = new();
        public Dictionary<string, bool> LayerVisibility { get => _layerVisibility; set => SetProperty(ref _layerVisibility, value); }

        [SettingHidden]
        private Dictionary<string, bool> _layerExpanded = new();
        public Dictionary<string, bool> LayerExpanded { get => _layerExpanded; set => SetProperty(ref _layerExpanded, value); }

        [SettingHidden]
        private string _landscapeCameraLocationString = "0x7D640013 [55.090 60.164 25.493] -0.164115 0.077225 -0.418708 0.889824";
        public string LandscapeCameraLocationString { get => _landscapeCameraLocationString; set => SetProperty(ref _landscapeCameraLocationString, value); }

        [SettingHidden]
        private bool _landscapeCameraIs3D = true;
        public bool LandscapeCameraIs3D { get => _landscapeCameraIs3D; set => SetProperty(ref _landscapeCameraIs3D, value); }

        [SettingHidden]
        private float _landscapeCameraMovementSpeed = 1000f;
        public float LandscapeCameraMovementSpeed { get => _landscapeCameraMovementSpeed; set => SetProperty(ref _landscapeCameraMovementSpeed, value); }

        [SettingHidden]
        private int _landscapeCameraFieldOfView = 60;
        public int LandscapeCameraFieldOfView { get => _landscapeCameraFieldOfView; set => SetProperty(ref _landscapeCameraFieldOfView, value); }


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

    [SettingCategory("Graphics", ParentCategory = "Project", Order = 0)]
    public partial class ProjectGraphicsSettings : ObservableObject {
        [SettingDisplayName("Anisotropic Filtering")]
        [SettingDescription("Improves texture clarity at sharp viewing angles.")]
        private bool _enableAnisotropicFiltering = true;
        public bool EnableAnisotropicFiltering { 
            get => _enableAnisotropicFiltering; 
            set => SetProperty(ref _enableAnisotropicFiltering, value); 
        }
    }

    [SettingCategory("Export", ParentCategory = "Project", Order = 1)]
    public partial class ProjectExportSettings : ObservableObject {
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
    }

    [SettingCategory("Landscape Tools", ParentCategory = "Project", Order = 2)]
    public partial class LandscapeToolsSettings : ObservableObject {
        [SettingDisplayName("Saved Brush Size")]
        [SettingDescription("Default brush size for landscape tools.")]
        [SettingRange(1.0, 50.0, 1.0, 5.0)]
        private float _brushSize = 5.0f;
        public float BrushSize { get => _brushSize; set => SetProperty(ref _brushSize, value); }

        [SettingDisplayName("Saved Tool Filtering Option")]
        [SettingDescription("Default filtering option used by the landscape brush tools.")]
        private int _toolFilteringOption = 0; 
        public int ToolFilteringOption { get => _toolFilteringOption; set => SetProperty(ref _toolFilteringOption, value); }
    }
}