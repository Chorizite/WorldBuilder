using CommunityToolkit.Mvvm.ComponentModel;
using System.Numerics;

namespace WorldBuilder.Shared.Lib {
    /// <summary>
    /// Centralized state for the editor's rendering and tools.
    /// </summary>
    public partial class EditorState : ObservableObject {
        [ObservableProperty] private bool _showScenery = true;
        [ObservableProperty] private bool _showStaticObjects = true;
        [ObservableProperty] private bool _showBuildings = true;
        [ObservableProperty] private bool _showSkybox = true;
        [ObservableProperty] private bool _showDebugShapes = true;
        [ObservableProperty] private bool _showUnwalkableSlopes = false;
        [ObservableProperty] private bool _showGrid = true;
        [ObservableProperty] private bool _enableTransparencyPass = true;
        [ObservableProperty] private float _timeOfDay = 0.5f;
        [ObservableProperty] private float _lightIntensity = 1.0f;
        
        [ObservableProperty] private bool _showLandblockGrid = true;
        [ObservableProperty] private bool _showCellGrid = true;
        [ObservableProperty] private Vector3 _landblockGridColor = new(1, 0, 1);
        [ObservableProperty] private Vector3 _cellGridColor = new(0, 1, 1);
        [ObservableProperty] private float _gridLineWidth = 1.0f;
        [ObservableProperty] private float _gridOpacity = 0.4f;

        [ObservableProperty] private int _objectRenderDistance = 12;
        [ObservableProperty] private float _maxDrawDistance = 40000f;
    }
}
