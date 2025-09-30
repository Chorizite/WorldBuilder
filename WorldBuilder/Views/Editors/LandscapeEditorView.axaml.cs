using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Chorizite.Core.Render;
using Chorizite.OpenGLSDLBackend;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Tools;
using WorldBuilder.Tools.Landscape;
using WorldBuilder.ViewModels.Editors;
using WorldBuilder.ViewModels.Editors.LandscapeEditor;

namespace WorldBuilder.Views.Editors;

public partial class LandscapeEditorView : Base3DView {
    private GL _gl;
    public PixelSize CanvasSize { get; private set; }

    private Project _project;
    private IDatReaderWriter _dats;
    private IRenderer render;
    private FPSCounter _fpsCounter;
    internal RenderTarget RenderTarget;
    internal CameraManager _cameraManager;
    private TerrainRenderer _renderer;
    internal TerrainProvider _terrainGenerator;
    private TerrainDocument _terrain;
    internal TerrainEditingContext _editingContext;
    private bool _didInit;

    // Tool management
    private ITerrainTool _currentToolInstance;
    private TexturePaintingTool _texturePaintingTool;
    private RoadDrawingTool _roadDrawingTool;

    private PerspectiveCamera _perspectiveCamera;
    private OrthographicTopDownCamera _topDownCamera;
    private int _loadedChunks;
    private int _visibleChunks;
    private bool _isQPressedLastFrame;

    private LandscapeEditorViewModel ViewModel => (LandscapeEditorViewModel)DataContext;

    public LandscapeEditorView() {
        InitializeComponent();
        InitializeBase3DView();

        Console.WriteLine($"ADDING DataContext LandscapeEditorViewModel");
        DataContext = ProjectManager.Instance.GetProjectService<LandscapeEditorViewModel>();

        // Subscribe to tool changes
        if (ViewModel != null) {
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(LandscapeEditorViewModel.SelectedTool)) {
            OnToolChanged();
        }
    }

    private void OnToolChanged() {
        if (!_didInit || ViewModel?.SelectedTool == null) return;

        // Deactivate current tool
        _currentToolInstance?.OnDeactivated(_editingContext);

        // Switch to new tool
        if (ViewModel.SelectedTool is TexturePaintingToolViewModel) {
            _currentToolInstance = _texturePaintingTool;
            UpdateTexturePaintingToolFromViewModel();
        }
        else if (ViewModel.SelectedTool is RoadDrawingToolViewModel) {
            _currentToolInstance = _roadDrawingTool;
        }

        // Activate new tool
        _currentToolInstance?.OnActivated(_editingContext);

        // Subscribe to subtool changes
        if (ViewModel.SelectedTool != null) {
            ViewModel.SelectedTool.PropertyChanged -= SelectedTool_PropertyChanged;
            ViewModel.SelectedTool.PropertyChanged += SelectedTool_PropertyChanged;
            OnSubToolChanged();
        }
    }

    private void SelectedTool_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(ToolViewModelBase.SelectedSubTool)) {
            OnSubToolChanged();
        }
    }

    private void OnSubToolChanged() {
        if (!_didInit || ViewModel?.SelectedTool == null) return;

        if (ViewModel.SelectedTool is TexturePaintingToolViewModel textureVM) {
            UpdateTexturePaintingToolFromViewModel();
        }
        // Handle other tools as needed
    }

    private void UpdateTexturePaintingToolFromViewModel() {
        if (_texturePaintingTool == null || ViewModel?.SelectedTool?.SelectedSubTool == null) return;

        var subTool = ViewModel.SelectedTool.SelectedSubTool;

        if (subTool is BrushSubToolViewModel brushVM) {
            _texturePaintingTool.SetPaintMode(TexturePaintingTool.PaintSubMode.Brush);
            _texturePaintingTool.BrushRadius = brushVM.BrushRadius;
            _texturePaintingTool.SelectedTerrainType = brushVM.SelectedTerrainType;

            // Subscribe to property changes for real-time updates
            brushVM.PropertyChanged -= BrushVM_PropertyChanged;
            brushVM.PropertyChanged += BrushVM_PropertyChanged;
        }
        else if (subTool is BucketFillSubToolViewModel bucketVM) {
            _texturePaintingTool.SetPaintMode(TexturePaintingTool.PaintSubMode.Bucket);
            _texturePaintingTool.SelectedTerrainType = bucketVM.SelectedTerrainType;

            bucketVM.PropertyChanged -= BucketVM_PropertyChanged;
            bucketVM.PropertyChanged += BucketVM_PropertyChanged;
        }
    }

    private void BrushVM_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (_texturePaintingTool == null) return;
        var vm = (BrushSubToolViewModel)sender;

        if (e.PropertyName == nameof(BrushSubToolViewModel.BrushRadius)) {
            _texturePaintingTool.BrushRadius = vm.BrushRadius;
        }
        else if (e.PropertyName == nameof(BrushSubToolViewModel.SelectedTerrainType)) {
            _texturePaintingTool.SelectedTerrainType = vm.SelectedTerrainType;
        }
    }

    private void BucketVM_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (_texturePaintingTool == null) return;
        var vm = (BucketFillSubToolViewModel)sender;

        if (e.PropertyName == nameof(BucketFillSubToolViewModel.SelectedTerrainType)) {
            _texturePaintingTool.SelectedTerrainType = vm.SelectedTerrainType;
        }
    }

    protected override void OnGlInit(GL gl, PixelSize canvasSize) {
        _gl = gl;
        CanvasSize = canvasSize;
    }

    public void Init(Project project, OpenGLRenderer render) {
        _project = project;
        _dats = project.DocumentManager.Dats;
        this.render = render;

        _fpsCounter = new FPSCounter();

        var sw = Stopwatch.StartNew();
        _terrain = project.DocumentManager.GetOrCreateDocumentAsync<TerrainDocument>("terrain").Result;
        Console.WriteLine($"Loaded terrain in {sw.ElapsedMilliseconds}ms");

        sw.Restart();
        _terrainGenerator = new TerrainProvider(render, _terrain, _dats, 64);
        Console.WriteLine($"Initialized terrain generator in {sw.ElapsedMilliseconds}ms");

        // Initialize editing context
        _editingContext = new TerrainEditingContext(_terrain, _terrainGenerator);
        Console.WriteLine($"Initialized editing context in {sw.ElapsedMilliseconds}ms");

        // Initialize tool instances
        _texturePaintingTool = new TexturePaintingTool();
        _roadDrawingTool = new RoadDrawingTool();
        _currentToolInstance = _texturePaintingTool;

        _perspectiveCamera = new PerspectiveCamera(new Vector3(192f * 254f / 2f, 192f * 254f / 2f, 1000), Vector3.UnitZ);
        _topDownCamera = new OrthographicTopDownCamera(new Vector3(0, 0, 200f));

        Console.WriteLine($"Initialized cameras in {sw.ElapsedMilliseconds}ms");

        // Use camera manager
        _cameraManager = new CameraManager(_topDownCamera);
        Console.WriteLine($"Initialized camera manager in {sw.ElapsedMilliseconds}ms");

        _renderer = new TerrainRenderer(render, _terrainGenerator.LandSurf.TerrainAtlas, _terrainGenerator.LandSurf.AlphaAtlas);
        Console.WriteLine($"Initialized terrain renderer in {sw.ElapsedMilliseconds}ms");

        // Force initial chunk loading
        UpdateChunkGeneration();
        Console.WriteLine($"Initial chunk load in {sw.ElapsedMilliseconds}ms");

        // Initialize tool from view model
        OnToolChanged();
    }

    private void UpdateChunkGeneration() {
        if (_terrainGenerator == null) return;

        var view = _cameraManager.Current.GetViewMatrix();
        var projection = _cameraManager.Current.GetProjectionMatrix(((float)CanvasSize.Width / CanvasSize.Height), 1.0f, 80000f);
        var viewProjection = view * projection;

        _terrainGenerator.UpdateChunks(_cameraManager.Current.Position, viewProjection);

        _loadedChunks = _terrainGenerator.GetLoadedChunkCount();
        _visibleChunks = _terrainGenerator.GetVisibleChunkCount();
    }

    protected override void OnGlResize(PixelSize canvasSize) {
        CanvasSize = canvasSize;
    }

    protected override void OnGlRender(double deltaTime) {
        if (!_didInit && ProjectManager.Instance.CurrentProject?.DocumentManager != null) {
            Init(ProjectManager.Instance.CurrentProject, Renderer);
            _didInit = true;
        }

        if (_didInit) {
            try {
                _cameraManager.Current.ScreenSize = new Vector2(CanvasSize.Width, CanvasSize.Height);

                if (InputState.IsKeyDown(Key.Q)) {
                    if (!_isQPressedLastFrame) {
                        Console.WriteLine("Switching camera");
                        if (_cameraManager.Current == _perspectiveCamera) {
                            _cameraManager.SwitchCamera(_topDownCamera);
                        }
                        else {
                            _cameraManager.SwitchCamera(_perspectiveCamera);
                        }
                    }
                    _isQPressedLastFrame = true;
                }
                else {
                    _isQPressedLastFrame = false;
                }

                if (InputState.IsKeyDown(Key.W)) {
                    _cameraManager.Current.ProcessKeyboard(CameraMovement.Forward, deltaTime);
                }
                if (InputState.IsKeyDown(Key.S))
                    _cameraManager.Current.ProcessKeyboard(CameraMovement.Backward, deltaTime);
                if (InputState.IsKeyDown(Key.A))
                    _cameraManager.Current.ProcessKeyboard(CameraMovement.Left, deltaTime);
                if (InputState.IsKeyDown(Key.D))
                    _cameraManager.Current.ProcessKeyboard(CameraMovement.Right, deltaTime);

                _cameraManager.Current.ProcessMouseMovement(InputState.MouseState);

                _currentToolInstance?.Update(deltaTime, _editingContext);

                // Update graphics buffers for all modified chunks
                UpdateModifiedLandblocks();
                // Update chunk generation based on camera movement
                UpdateChunkGeneration();

                if (_renderer != null && _terrainGenerator != null) {
                    var visibleChunks = _terrainGenerator.GetVisibleChunks();
                    _renderer.RenderChunks(_cameraManager.Current, ((float)CanvasSize.Width / CanvasSize.Height), visibleChunks, _editingContext, CanvasSize.Width, CanvasSize.Height);
                }

                // Render tool overlay if the tool supports it
                if (_renderer != null && _currentToolInstance != null) {
                    _currentToolInstance.RenderOverlay(_editingContext, render, _cameraManager.Current, ((float)CanvasSize.Width / CanvasSize.Height));
                }
            }
            catch (Exception ex) {
                Console.WriteLine(ex.ToString());
            }
        }
    }

    private void UpdateModifiedLandblocks() {
        var modified = _editingContext.ModifiedLandblocks.ToArray();
        foreach (var lbId in modified) {
            _terrainGenerator.UpdateLandblock((uint)(lbId >> 8) & 0xFF, (uint)lbId & 0xFF);
        }
        _editingContext.ClearModifiedLandblocks();
    }

    protected override void OnGlKeyDown(KeyEventArgs e) {
        if (!_didInit) return;
    }

    protected override void OnGlKeyUp(KeyEventArgs e) {
        if (!_didInit) return;
    }

    protected override void OnGlPointerMoved(PointerEventArgs e, Vector2 mousePositionScaled) {
        if (!_didInit) return;

        if (_currentToolInstance != null) {
            bool handled = _currentToolInstance.HandleMouseMove(InputState.MouseState, _editingContext);
        }
    }

    protected override void OnGlPointerWheelChanged(PointerWheelEventArgs e) {
        if (!_didInit) return;

        if (_cameraManager.Current is PerspectiveCamera perspectiveCamera) {
            perspectiveCamera.ProcessMouseScroll((float)e.Delta.Y);
        }
        else if (_cameraManager.Current is OrthographicTopDownCamera orthoCamera) {
            orthoCamera.ProcessMouseScrollAtCursor((float)e.Delta.Y, InputState.MouseState.Position, new Vector2(CanvasSize.Width, CanvasSize.Height));
        }
    }

    protected override void OnGlPointerPressed(PointerPressedEventArgs e) {
        if (!_didInit) return;

        if (_currentToolInstance != null) {
            bool handled = _currentToolInstance.HandleMouseDown(InputState.MouseState, _editingContext);
        }
    }

    protected override void OnGlPointerReleased(PointerReleasedEventArgs e) {
        if (!_didInit) return;

        if (_currentToolInstance != null) {
            bool handled = _currentToolInstance.HandleMouseUp(InputState.MouseState, _editingContext);
        }
    }

    protected override void UpdateMouseState(Point position, PointerPointProperties properties) {
        base.UpdateMouseState(position, properties);

        if (!_didInit) return;

        var controlWidth = (int)Bounds.Width;
        var controlHeight = (int)Bounds.Height;

        var clampedPosition = new Point(
            Math.Max(0, Math.Min(controlWidth - 1, position.X)),
            Math.Max(0, Math.Min(controlHeight - 1, position.Y))
        );

        InputState.UpdateMouseState(
            clampedPosition,
            properties,
            CanvasSize.Width,
            CanvasSize.Height,
            InputScale,
            _cameraManager?.Current,
            _terrainGenerator);
    }

    protected override void OnGlDestroy() {
        RenderTarget?.Dispose();
        _renderer?.Dispose();
        _terrainGenerator?.Dispose();

        // Unsubscribe from events
        if (ViewModel != null) {
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            if (ViewModel.SelectedTool != null) {
                ViewModel.SelectedTool.PropertyChanged -= SelectedTool_PropertyChanged;
            }
        }
    }
}