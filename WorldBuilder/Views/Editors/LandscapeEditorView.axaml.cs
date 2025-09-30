using Autofac.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Chorizite.Core.Render;
using Chorizite.OpenGLSDLBackend;
using Microsoft.Extensions.Logging;
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

    // Tools
    private readonly Dictionary<string, ITerrainTool> _tools = new();
    private ITerrainTool _currentTool;
    private PerspectiveCamera _perspectiveCamera;
    private OrthographicTopDownCamera _topDownCamera;
    private int _loadedChunks;
    private int _visibleChunks;
    private bool _isQPressedLastFrame;

    public LandscapeEditorView() {
        InitializeComponent();
        InitializeBase3DView();

        Console.WriteLine($"ADDING DataContext LandscapeEditorViewModel");
        DataContext = ProjectManager.Instance.GetProjectService<LandscapeEditorViewModel>();
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

        // Initialize tools
        _tools["Textures"] = new TexturePaintingTool();
        _tools["Roads"] = new RoadDrawingTool();

        _currentTool = _tools["Textures"];


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
    }

    private void UpdateChunkGeneration() {
        if (_terrainGenerator == null) return;

        // Create view-projection matrix for frustum culling
        var view = _cameraManager.Current.GetViewMatrix();
        var projection = _cameraManager.Current.GetProjectionMatrix(((float)CanvasSize.Width / CanvasSize.Height), 1.0f, 80000f);
        var viewProjection = view * projection;

        // Update chunks based on camera position
        _terrainGenerator.UpdateChunks(_cameraManager.Current.Position, viewProjection);

        // Update performance metrics
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
        
        if (_didInit){
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
                    // Prevent switching again until key is released
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

                _currentTool?.Update(deltaTime, _editingContext);

                // Update graphics buffers for all modified chunks
                UpdateModifiedLandblocks();
                // Update chunk generation based on camera movement
                UpdateChunkGeneration();
                //render.UIShader.Bind();
                //render.GraphicsDevice.CullMode = CullMode.Back;

                if (_renderer != null && _terrainGenerator != null) {
                    var visibleChunks = _terrainGenerator.GetVisibleChunks();
                    _renderer.RenderChunks(_cameraManager.Current, ((float)CanvasSize.Width / CanvasSize.Height), visibleChunks, _editingContext, CanvasSize.Width, CanvasSize.Height);
                }

                // Render tool overlay if the tool supports it
                if (_renderer != null && _currentTool != null) {
                    _currentTool.RenderOverlay(_editingContext, render, _cameraManager.Current, ((float)CanvasSize.Width / CanvasSize.Height));
                }

                //_fpsCounter.UpdateFPS(deltaTime);
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

        if (_currentTool != null) {
            bool handled = _currentTool.HandleMouseMove(InputState.MouseState, _editingContext);
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

        if (_currentTool != null) {
            bool handled = _currentTool.HandleMouseDown(InputState.MouseState, _editingContext);
        }
    }

    protected override void OnGlPointerReleased(PointerReleasedEventArgs e) {
        if (!_didInit) return;

        if (_currentTool != null) {
            bool handled = _currentTool.HandleMouseUp(InputState.MouseState, _editingContext);
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
    }
}