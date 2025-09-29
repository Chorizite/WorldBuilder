using Autofac.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Chorizite.Core.Render;
using Chorizite.OpenGLSDLBackend;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using SkiaSharp;
using System;
using System.IO;
using System.Numerics;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Lib.Extensions;
using WorldBuilder.Shared.Models;
using WorldBuilder.Tools;
using WorldBuilder.Tools.Landscape;
using static Avalonia.OpenGL.GlConsts;

namespace WorldBuilder;

public partial class OpenGlLeasePage : UserControl {
    private Control _viewport;
    private GlVisual _glVisual;
    private CompositionCustomVisual? _visual;
    public AvaloniaInputState InputState { get; } = new();
    private bool _hasPointer;
    private Vector2 _lastMousePosition;

    public OpenGlLeasePage() {
        InitializeComponent();
        this.AttachedToVisualTree += (s, e) => this.Focus();
        _viewport.AttachedToVisualTree += ViewportAttachedToVisualTree;
        _viewport.DetachedFromVisualTree += ViewportDetachedFromVisualTree;
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
        Focusable = true;
        Background = Brushes.Transparent; // Required for mouse input events

        _viewport = this.FindControl<Control>("Viewport")!;
        _hasPointer = true;
    }

    #region Input Event Handlers

    protected override void OnKeyDown(KeyEventArgs e) {
        base.OnKeyDown(e);
        if (IsEffectivelyVisible)
            InputState.SetKey(e.Key, true);
    }

    protected override void OnKeyUp(KeyEventArgs e) {
        base.OnKeyUp(e);
        if (IsEffectivelyVisible) {
            InputState.Modifiers = e.KeyModifiers;
            InputState.SetKey(e.Key, false);
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e) {
        base.OnPointerEntered(e);
        _hasPointer = true;
    }

    protected override void OnPointerExited(PointerEventArgs e) {
        base.OnPointerExited(e);
        _hasPointer = false;
    }

    protected override void OnPointerMoved(PointerEventArgs e) {
        if (!IsValidForInput()) return;

        try {
            var position = e.GetPosition(this);
            UpdateMouseState(position, e.Properties);

//            if (_glVisual._tool?.HandleMouseMove(e, _glVisual._tool._editingContext, InputState.MouseState) == true) {
//                _lastMousePosition = new Vector2((float)position.X, (float)position.Y);
//            }
        }
        catch (Exception ex) {
            LogException(ex);
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e) {
        base.OnPointerWheelChanged(e);

        if (_glVisual != null) {
            var position = e.GetPosition(this);
            var adjustedPosition = new Vector2((float)position.X, (float)position.Y);
            //_glVisual._tool?.OnMouseScroll(e, adjustedPosition);
        }
    }

    private bool IsValidForInput() =>
        IsEffectivelyVisible &&
        _hasPointer &&
        _glVisual?._tool != null;

    protected override void OnPointerPressed(PointerPressedEventArgs e) {
        base.OnPointerPressed(e);
        if (!IsValidForInput()) return;

        try {
            var position = e.GetPosition(this);
            UpdateMouseState(position, e.Properties);
            //_glVisual._tool?.HandleMouseDown(InputState.MouseState, _glVisual._tool._editingContext);
        }
        catch (Exception ex) {
            LogException(ex);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e) {
        base.OnPointerReleased(e);
        if (!IsValidForInput()) return;

        try {
            var position = e.GetPosition(this);
            UpdateMouseState(position, e.Properties);
            //_glVisual._tool?.HandleMouseUp(InputState.MouseState, _glVisual._tool._editingContext);
        }
        catch (Exception ex) {
            LogException(ex);
        }
    }

    #endregion

    #region Helper Methods

    private void UpdateMouseState(Point position, PointerPointProperties properties) {
        if (_glVisual == null) return;

        var controlWidth = (int)Bounds.Width;
        var controlHeight = (int)Bounds.Height;

        var clampedPosition = new Point(
            Math.Max(0, Math.Min(controlWidth - 1, position.X)),
            Math.Max(0, Math.Min(controlHeight - 1, position.Y))
        );

        InputState.UpdateMouseState(
            clampedPosition,
            properties,
            controlWidth,
            controlHeight,
            _glVisual._tool._cameraManager.Current,
            _glVisual._tool._terrainGenerator);
    }

    private static void LogException(Exception ex) {
        Console.WriteLine($"Error in OpenGlLeasePage: {ex}");
    }

    public bool HitTest(Point point) => Bounds.Contains(point);

    #endregion

    #region Visual Tree Management

    private void ViewportAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
        var visual = ElementComposition.GetElementVisual(_viewport);
        if (visual == null) return;

        _glVisual = new GlVisual(InputState);
        _visual = visual.Compositor.CreateCustomVisual(_glVisual);
        ElementComposition.SetElementChildVisual(_viewport, _visual);
        UpdateSize(Bounds.Size);
    }

    private void ViewportDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
        _visual?.SendHandlerMessage(new DisposeMessage());
        _visual = null;
        ElementComposition.SetElementChildVisual(_viewport, null);
    }

    private void UpdateSize(Size size) {
        if (_visual != null)
            _visual.Size = new Avalonia.Vector(size.Width, size.Height);
    }

    protected override Size ArrangeOverride(Size finalSize) {
        var size = base.ArrangeOverride(finalSize);
        UpdateSize(size);
        return size;
    }

    #endregion

    #region GlVisual Class

    private class GlVisual : CompositionCustomVisualHandler {
        internal LandscapeTool _tool;
        private OpenGLRenderer _renderer;
        private bool _contentInitialized;
        private IGlContext? _gl;
        private readonly AvaloniaInputState _keyboardState;
        private DocumentDbContext _dbContext;

        public Project? Project { get; private set; }
        public ServiceProvider Services { get; }
        public DateTime LastRenderTime { get; private set; } = DateTime.MinValue;
        public ITexture? Texture => _tool?.RenderTarget?.Texture;

        public GlVisual(AvaloniaInputState keyboardState) {
            _keyboardState = keyboardState;
            Services = CreateServiceProvider();
        }

        private static ServiceProvider CreateServiceProvider() {
            var collection = new ServiceCollection();
            collection.AddLogging(o => {
                o.ClearProviders();
                o.AddProvider(new ColorConsoleLoggerProvider());
            });
            collection.AddWorldBuilder();
            return collection.BuildServiceProvider();
        }

        public override void OnRender(ImmediateDrawingContext drawingContext) {
            try {
                var frameTime = CalculateFrameTime();
                _keyboardState.OnFrame();
                RegisterForNextAnimationFrameUpdate();

                var bounds = GetRenderBounds();
                var size = PixelSize.FromSize(bounds.Size, 1);

                if (size.Width < 1 || size.Height < 1) return;

                if (drawingContext.TryGetFeature<ISkiaSharpApiLeaseFeature>(out var skiaFeature)) {
                    using var skiaLease = skiaFeature.Lease();
                    var grContext = skiaLease.GrContext;
                    if (grContext == null) return;

                    var dst = skiaLease.SkCanvas.DeviceClipBounds;
                    var canvasSize = new PixelSize(dst.Right - dst.Left, dst.Bottom - dst.Top);

                    using (var platformApiLease = skiaLease.TryLeasePlatformGraphicsApi()) {
                        if (platformApiLease?.Context is not IGlContext glContext) return;

                        if (_gl != glContext) {
                            _contentInitialized = false;
                            _gl = glContext;
                        }

                        var gl = GL.GetApi(glContext.GlInterface.GetProcAddress);

                        glContext.GlInterface.GetIntegerv(GL_FRAMEBUFFER_BINDING, out var oldFb);
                        SetDefaultStates(gl);
                        if (!_contentInitialized) {
                            InitializeContent(gl, size);
                        }

                        gl.Viewport(0, 0, (uint)dst.Width, (uint)dst.Height);

                        RenderFrame(gl, canvasSize, frameTime, grContext);
                        glContext.GlInterface.BindFramebuffer(GL_FRAMEBUFFER, oldFb);

                        if (_tool.RenderTarget is not null) {
                            var textureFB = (int?)_tool.RenderTarget.Framebuffer?.NativeHandle ?? 0;
                            glContext.GlInterface.BindFramebuffer(GL_READ_FRAMEBUFFER, textureFB);
                            glContext.GlInterface.BindFramebuffer(GL_DRAW_FRAMEBUFFER, oldFb);

                            var srcWidth = _tool.RenderTarget.Texture.Width;
                            var srcHeight = _tool.RenderTarget.Texture.Height;

                            glContext.GlInterface.BlitFramebuffer(
                                0, 0, srcWidth, srcHeight,
                                dst.Left, dst.Top, dst.Right, dst.Bottom,
                                GL_COLOR_BUFFER_BIT,
                                GL_LINEAR
                            );
                        }
                    }
                }
            }
            catch (Exception ex) {
                Console.WriteLine($"Render error: {ex}");
            }
        }

        public static void SetDefaultStates(GL gl) {
            // === DEPTH TESTING ===
            gl.Enable(EnableCap.DepthTest);
            gl.DepthFunc(DepthFunction.Greater);       // Standard depth test (Less for normal Z, Greater for reverse-Z)
            //gl.DepthMask(false);                     // Allow depth writes
            //gl.ClearDepth(0f);

            gl.Enable(EnableCap.CullFace);
            gl.CullFace(TriangleFace.Back);            // Cull back faces
            gl.FrontFace(FrontFaceDirection.Ccw);      // Counter-clockwise winding = front face

            gl.Disable(EnableCap.Blend);               // Disable for opaque objects
            gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha); // Standard alpha blending
            gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        }

        private double CalculateFrameTime() {
            var now = DateTime.Now;
            var frameTime = LastRenderTime == DateTime.MinValue ? 0 : (now - LastRenderTime).TotalSeconds;
            LastRenderTime = now;
            return frameTime;
        }

        private void InitializeContent(GL gl, PixelSize size) {
            _contentInitialized = true;

            // Load project
            LoadDefaultProject();

            // Initialize tool and renderer
            _tool = new LandscapeTool();
            _renderer = new OpenGLRenderer(gl, Services.GetRequiredService<ILogger<OpenGLRenderer>>(), null, size.Width, size.Height);
            _tool.Init(Project, _renderer);
        }


        private void RenderFrame(GL gl, PixelSize size, double frameTime, GRContext grContext) {
            if (_tool == null) return;

            // Update tool
            _tool.Width = size.Width;
            _tool.Height = size.Height;
            _tool.Update(frameTime, _keyboardState);
            _tool.Render();
        }

        private void LoadDefaultProject() {
            LoadProject("Test", Path.Combine(GetProjectsDirectory(), "Test", "Test.json"));
        }

        private static string GetProjectsDirectory() {
            var projectsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Projects");
            if (!Directory.Exists(projectsDir)) {
                Directory.CreateDirectory(projectsDir);
            }
            return projectsDir;
        }

        public void LoadProject(string projectName, string projectFilePath) {
            const string baseDatDir = @"C:\Turbine\Asheron's Call\";

            if (!Directory.Exists(baseDatDir)) {
                throw new DirectoryNotFoundException($"Base dat directory not found: {baseDatDir}");
            }

            Project = File.Exists(projectFilePath)
                ? Project.FromDisk(projectFilePath)
                : Project.Create(projectName, projectFilePath, baseDatDir);

            if (Project != null) {
                _dbContext = UpdateDatabaseConnection(Project.DatabasePath);
                InitializeDocumentManager();
            }
        }

        private void InitializeDocumentManager() {
            var documentLogger = Services.GetService<ILogger<DocumentManager>>();
            var storageLogger = Services.GetService<ILogger<DocumentStorageService>>();

            Project.DocumentManager = new DocumentManager(
                new DocumentStorageService(_dbContext, storageLogger),
                documentLogger);
            Project.DocumentManager.Dats = Project.DatReaderWriter;
        }

        private DocumentDbContext UpdateDatabaseConnection(string databasePath) {
            var serviceCollection = new ServiceCollection();
            var connectionString = $"DataSource={databasePath}";

            serviceCollection.AddDbContext<DocumentDbContext>(
                o => o.UseSqlite(connectionString),
                ServiceLifetime.Scoped);

            var serviceProvider = serviceCollection.BuildServiceProvider();
            var dbContext = serviceProvider.GetRequiredService<DocumentDbContext>();
            dbContext.Database.EnsureCreated();

            return dbContext;
        }

        public override void OnAnimationFrameUpdate() {
            Invalidate();
            base.OnAnimationFrameUpdate();
        }

        public override void OnMessage(object message) {
            if (message is DisposeMessage) {
                DisposeResources();
            }
            base.OnMessage(message);
        }

        private void DisposeResources() {
            if (_gl == null) return;

            try {
                if (_contentInitialized) {
                    using (_gl.MakeCurrent()) {
                        // Dispose OpenGL resources
                        var gl = GL.GetApi(_gl.GlInterface.GetProcAddress);


                        _contentInitialized = false;
                    }
                }
            }
            catch (Exception ex) {
                Console.WriteLine($"Error disposing resources: {ex}");
            }
            finally {
                _gl = null;
            }
        }
    }

    #endregion

    public class DisposeMessage { }
}