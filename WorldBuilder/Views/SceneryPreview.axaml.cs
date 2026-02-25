using Avalonia;
using Avalonia.Threading;
using Chorizite.OpenGLSDLBackend;
using DatReaderWriter.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using System;
using System.Linq;
using System.Numerics;
using WorldBuilder.Lib;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Modules.Landscape;
using WorldBuilder.Services;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Views;

public partial class SceneryPreview : Base3DViewport {
    private GL? _gl;
    private GameScene? _gameScene;
    private LandscapeDocument? _previewDoc;
    private PreviewRegionInfo? _previewRegion;

    private TerrainTextureType _cachedTexture;
    private byte _cachedSceneryIndex;
    private IDatReaderWriter? _cachedDats;
    private bool _needsUpdate;
    private double _totalTime;

    public static readonly StyledProperty<TerrainTextureType> TextureProperty =
        AvaloniaProperty.Register<SceneryPreview, TerrainTextureType>(nameof(Texture));

    public TerrainTextureType Texture {
        get => GetValue(TextureProperty);
        set => SetValue(TextureProperty, value);
    }

    public static readonly StyledProperty<byte> SceneryIndexProperty =
        AvaloniaProperty.Register<SceneryPreview, byte>(nameof(SceneryIndex));

    public byte SceneryIndex {
        get => GetValue(SceneryIndexProperty);
        set => SetValue(SceneryIndexProperty, value);
    }

    public static readonly StyledProperty<IDatReaderWriter?> DatsProperty =
        AvaloniaProperty.Register<SceneryPreview, IDatReaderWriter?>(nameof(Dats));

    public IDatReaderWriter? Dats {
        get => GetValue(DatsProperty);
        set => SetValue(DatsProperty, value);
    }

    public SceneryPreview() {
        InitializeComponent();
        InitializeBase3DView();
    }

    protected override void OnGlInit(GL gl, PixelSize canvasSize) {
        _gl = gl;
        var loggerFactory = WorldBuilder.App.Services?.GetService<ILoggerFactory>() ?? LoggerFactory.Create(builder => {
            builder.AddProvider(new ColorConsoleLoggerProvider());
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        var portalService = WorldBuilder.App.Services?.GetService<ProjectManager>()?.GetProjectService<IPortalService>() ?? 
                            WorldBuilder.App.Services?.GetService<IPortalService>() ?? 
                            new PortalService(Dats ?? WorldBuilder.App.Services?.GetService<ProjectManager>()?.GetProjectService<IDatReaderWriter>()!);
        _gameScene = new GameScene(gl, Renderer!.GraphicsDevice, loggerFactory, portalService);
        _gameScene.Initialize();
        _gameScene.Resize(canvasSize.Width, canvasSize.Height);
        _gameScene.SetCameraMode(true);

        // Increase render distances to ensure the preview landblock is always loaded
        _gameScene.State.MaxDrawDistance = 10000f;
        _gameScene.State.ObjectRenderDistance = 5;

        var settings = WorldBuilder.App.Services?.GetService<WorldBuilderSettings>();
        if (settings != null) {
            _gameScene.State.EnableTransparencyPass = settings.Landscape.Rendering.EnableTransparencyPass;
        }

        _needsUpdate = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
        base.OnPropertyChanged(change);
        if (change.Property == TextureProperty || change.Property == SceneryIndexProperty || change.Property == DatsProperty) {
            _cachedTexture = Texture;
            _cachedSceneryIndex = SceneryIndex;
            _cachedDats = Dats;
            _needsUpdate = true;
        }
    }

    private void UpdatePreview() {
        if (_gameScene == null || _cachedDats == null) return;

        if (_previewDoc == null) {
            if (_cachedDats.CellRegions.Count > 0) {
                var regionId = _cachedDats.CellRegions.Keys.FirstOrDefault();
                if (_cachedDats.RegionFileMap.TryGetValue(regionId, out var regionFileId)) {
                    if (_cachedDats.Portal.TryGet<DatReaderWriter.DBObjs.Region>(regionFileId, out var region)) {
                        _previewRegion = new PreviewRegionInfo(new RegionInfo(region));
                        _previewDoc = new LandscapeDocument(regionId) {
                            Region = _previewRegion,
                            CellDatabase = _cachedDats.CellRegions.TryGetValue(regionId, out var cellDb) ? cellDb : null,
                        };
                        var layer = new LandscapeLayer("Preview", true);
                        _previewDoc.LayerTree.Add(layer);
                    }
                }
            }
        }

        if (_previewDoc != null && _previewRegion != null) {
            var layer = _previewDoc.GetAllLayers().FirstOrDefault() as LandscapeLayer;
            if (layer != null) {
                // Fill with desired texture and scenery
                for (uint vx = 0; vx < 9; vx++) {
                    for (uint vy = 0; vy < 9; vy++) {
                        uint idx = vy * (uint)_previewRegion.MapWidthInVertices + vx;
                        _previewDoc.SetVertex(layer.Id, idx, new TerrainEntry {
                            Type = (byte)_cachedTexture,
                            Scenery = (_cachedSceneryIndex == 255) ? (byte?)null : _cachedSceneryIndex,
                            Height = 0,
                            Road = 0
                        });
                    }
                }
                _previewDoc.RecalculateTerrainCache();
            }

            var projectManager = WorldBuilder.App.Services?.GetService<ProjectManager>();
            var meshManagerService = projectManager?.GetProjectService<MeshManagerService>();
            var meshManager = meshManagerService?.GetMeshManager(Renderer!.GraphicsDevice, _cachedDats);

            var documentManager = projectManager?.GetProjectService<IDocumentManager>();
            _gameScene.SetLandscape(_previewDoc, _cachedDats, documentManager!, meshManager);
            _gameScene.InvalidateLandblock(0, 0);
        }
        _needsUpdate = false;
    }

    protected override void OnGlRender(double frameTime) {
        if (_gl == null || _gameScene == null) return;

        if (_needsUpdate) {
            UpdatePreview();
        }

        _totalTime += frameTime;
        if (_gameScene.CurrentCamera is Camera3D cam3d) {
            float speed = 40f; // degrees per second
            float angleDegrees = (float)(_totalTime * speed) % 360f;
            float angleRad = angleDegrees * MathF.PI / 180f;

            float baseRadius = 130f;
            float radiusAmplitude = 100f;
            float radius = baseRadius + MathF.Sin((float)_totalTime * 0.5f) * radiusAmplitude;

            float baseHeight = 90f;
            float heightAmplitude = 70f;
            float height = baseHeight + MathF.Cos((float)_totalTime * 0.3f) * heightAmplitude;

            // Rotate around origin (0,0,0)
            cam3d.Position = new Vector3(MathF.Sin(angleRad) * radius, -MathF.Cos(angleRad) * radius, height);

            // Point back to origin
            cam3d.LookAt(Vector3.Zero);
        }

        _gameScene.Update((float)frameTime);
        _gameScene.Render();
    }

    protected override void OnGlResize(PixelSize canvasSize) {
        _gameScene?.Resize(canvasSize.Width, canvasSize.Height);
    }

    protected override void OnGlDestroy() {
        _gameScene?.Dispose();
        _gameScene = null;
    }

    protected override void OnGlKeyDown(Avalonia.Input.KeyEventArgs e) { }
    protected override void OnGlKeyUp(Avalonia.Input.KeyEventArgs e) { }
    protected override void OnGlPointerMoved(Avalonia.Input.PointerEventArgs e, Vector2 mousePositionScaled) { }
    protected override void OnGlPointerWheelChanged(Avalonia.Input.PointerWheelEventArgs e) { }
    protected override void OnGlPointerPressed(Avalonia.Input.PointerPressedEventArgs e) { }
    protected override void OnGlPointerReleased(Avalonia.Input.PointerReleasedEventArgs e) { }
}
