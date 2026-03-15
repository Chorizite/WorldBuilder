using System;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using WorldBuilder.Shared.Lib.Settings;

using WorldBuilder.Shared.Modules.Landscape.Tools.Gizmo;

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
        private double _settingsWindowWidth = 700;
        public double SettingsWindowWidth { get => _settingsWindowWidth; set => SetProperty(ref _settingsWindowWidth, value); }

        [SettingHidden]
        private double _settingsWindowHeight = 500;
        public double SettingsWindowHeight { get => _settingsWindowHeight; set => SetProperty(ref _settingsWindowHeight, value); }

        [SettingHidden]
        private double _settingsWindowX = double.NaN;
        public double SettingsWindowX { get => _settingsWindowX; set => SetProperty(ref _settingsWindowX, value); }

        [SettingHidden]
        private double _settingsWindowY = double.NaN;
        public double SettingsWindowY { get => _settingsWindowY; set => SetProperty(ref _settingsWindowY, value); }

        [SettingHidden]
        private bool _settingsWindowIsMaximized = false;
        public bool SettingsWindowIsMaximized { get => _settingsWindowIsMaximized; set => SetProperty(ref _settingsWindowIsMaximized, value); }

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

        [SettingHidden]
        private string _activeTab = "Layers";
        public string ActiveTab { get => _activeTab; set => SetProperty(ref _activeTab, value); }


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

        [SettingDescription("Last cell iteration used for DAT export")]
        private int _lastDatExportCellIteration = 0;
        public int LastDatExportCellIteration { get => _lastDatExportCellIteration; set => SetProperty(ref _lastDatExportCellIteration, value); }
    }

    public partial class LandscapeToolsSettings : ObservableObject {
        [SettingHidden]
        private BrushToolSettings _brushTool = new();
        public BrushToolSettings BrushTool {
            get => _brushTool;
            set {
                if (_brushTool != null) _brushTool.PropertyChanged -= OnSubSettingsPropertyChanged;
                if (SetProperty(ref _brushTool, value) && _brushTool != null) {
                    _brushTool.PropertyChanged += OnSubSettingsPropertyChanged;
                }
            }
        }

        [SettingHidden]
        private BucketFillToolSettings _bucketFillTool = new();
        public BucketFillToolSettings BucketFillTool {
            get => _bucketFillTool;
            set {
                if (_bucketFillTool != null) _bucketFillTool.PropertyChanged -= OnSubSettingsPropertyChanged;
                if (SetProperty(ref _bucketFillTool, value) && _bucketFillTool != null) {
                    _bucketFillTool.PropertyChanged += OnSubSettingsPropertyChanged;
                }
            }
        }

        [SettingHidden]
        private ObjectManipulationToolSettings _objectManipulationTool = new();
        public ObjectManipulationToolSettings ObjectManipulationTool {
            get => _objectManipulationTool;
            set {
                if (_objectManipulationTool != null) _objectManipulationTool.PropertyChanged -= OnSubSettingsPropertyChanged;
                if (SetProperty(ref _objectManipulationTool, value) && _objectManipulationTool != null) {
                    _objectManipulationTool.PropertyChanged += OnSubSettingsPropertyChanged;
                }
            }
        }

        [SettingHidden]
        private InspectorToolSettings _inspectorTool = new();
        public InspectorToolSettings InspectorTool {
            get => _inspectorTool;
            set {
                if (_inspectorTool != null) _inspectorTool.PropertyChanged -= OnSubSettingsPropertyChanged;
                if (SetProperty(ref _inspectorTool, value) && _inspectorTool != null) {
                    _inspectorTool.PropertyChanged += OnSubSettingsPropertyChanged;
                }
            }
        }

        private void OnSubSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { }
    }

    public partial class BrushToolSettings : ObservableObject {
        private int _brushSize = 1;
        public int BrushSize { get => _brushSize; set => SetProperty(ref _brushSize, value); }

        private int _texture = 0;
        public int Texture { get => _texture; set => SetProperty(ref _texture, value); }

        private int _selectedScenery = 255;
        public int SelectedScenery { get => _selectedScenery; set => SetProperty(ref _selectedScenery, value); }
    }

    public partial class BucketFillToolSettings : ObservableObject {
        private bool _isContiguous = true;
        public bool IsContiguous { get => _isContiguous; set => SetProperty(ref _isContiguous, value); }

        private bool _onlyFillSameScenery = false;
        public bool OnlyFillSameScenery { get => _onlyFillSameScenery; set => SetProperty(ref _onlyFillSameScenery, value); }

        private int _texture = 0;
        public int Texture { get => _texture; set => SetProperty(ref _texture, value); }

        private int _selectedScenery = 255;
        public int SelectedScenery { get => _selectedScenery; set => SetProperty(ref _selectedScenery, value); }
    }

    public partial class ObjectManipulationToolSettings : ObservableObject {
        private bool _alignToSurface = false;
        public bool AlignToSurface { get => _alignToSurface; set => SetProperty(ref _alignToSurface, value); }

        private bool _showBoundingBoxes = false;
        public bool ShowBoundingBoxes { get => _showBoundingBoxes; set => SetProperty(ref _showBoundingBoxes, value); }

        private bool _isLocalSpace = false;
        public bool IsLocalSpace { get => _isLocalSpace; set => SetProperty(ref _isLocalSpace, value); }

        private GizmoMode _mode = GizmoMode.Translate;
        public GizmoMode Mode { get => _mode; set => SetProperty(ref _mode, value); }
    }

    public partial class InspectorToolSettings : ObservableObject {
        private bool _selectVertices = false;
        public bool SelectVertices { get => _selectVertices; set => SetProperty(ref _selectVertices, value); }

        private bool _selectBuildings = true;
        public bool SelectBuildings { get => _selectBuildings; set => SetProperty(ref _selectBuildings, value); }

        private bool _selectStaticObjects = true;
        public bool SelectStaticObjects { get => _selectStaticObjects; set => SetProperty(ref _selectStaticObjects, value); }

        private bool _selectScenery = false;
        public bool SelectScenery { get => _selectScenery; set => SetProperty(ref _selectScenery, value); }

        private bool _selectEnvCells = true;
        public bool SelectEnvCells { get => _selectEnvCells; set => SetProperty(ref _selectEnvCells, value); }

        private bool _selectEnvCellStaticObjects = true;
        public bool SelectEnvCellStaticObjects { get => _selectEnvCellStaticObjects; set => SetProperty(ref _selectEnvCellStaticObjects, value); }

        private bool _selectPortals = true;
        public bool SelectPortals { get => _selectPortals; set => SetProperty(ref _selectPortals, value); }

        private bool _showBoundingBoxes = true;
        public bool ShowBoundingBoxes { get => _showBoundingBoxes; set => SetProperty(ref _showBoundingBoxes, value); }
    }
}