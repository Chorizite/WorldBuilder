using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Chorizite.OpenGLSDLBackend;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.OpenGL;
using System;
using System.Numerics;
using WorldBuilder.Lib;
using WorldBuilder.Services;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Views {
    public partial class DatObjectViewer : Base3DViewport {
        private GL? _gl;
        private SingleObjectScene? _scene;
        private Vector2 _lastPointerPosition;

        // Thread-safe copies for the render thread
        private IDatReaderWriter? _renderDats;
        private uint _renderFileId;
        private bool _renderIsSetup;
        private bool _renderIsAutoCamera = true;
        private bool _renderShowWireframe;
        private bool _renderShowCulling = true;
        private Vector4 _renderBackgroundColor = new Vector4(0.15f, 0.15f, 0.2f, 1.0f);

        public static readonly StyledProperty<uint> FileIdProperty =
            AvaloniaProperty.Register<DatObjectViewer, uint>(nameof(FileId));

        public uint FileId {
            get => GetValue(FileIdProperty);
            set => SetValue(FileIdProperty, value);
        }

        public static readonly StyledProperty<bool> IsSetupProperty =
            AvaloniaProperty.Register<DatObjectViewer, bool>(nameof(IsSetup));

        public bool IsSetup {
            get => GetValue(IsSetupProperty);
            set => SetValue(IsSetupProperty, value);
        }

        public static readonly StyledProperty<IDatReaderWriter?> DatsProperty =
            AvaloniaProperty.Register<DatObjectViewer, IDatReaderWriter?>(nameof(Dats));

        public IDatReaderWriter? Dats {
            get => GetValue(DatsProperty);
            set => SetValue(DatsProperty, value);
        }

        public static readonly StyledProperty<bool> IsAutoCameraProperty =
            AvaloniaProperty.Register<DatObjectViewer, bool>(nameof(IsAutoCamera), true);

        public bool IsAutoCamera {
            get => GetValue(IsAutoCameraProperty);
            set => SetValue(IsAutoCameraProperty, value);
        }

        public static readonly StyledProperty<bool> ShowWireframeProperty =
            AvaloniaProperty.Register<DatObjectViewer, bool>(nameof(ShowWireframe), false);

        public bool ShowWireframe {
            get => GetValue(ShowWireframeProperty);
            set => SetValue(ShowWireframeProperty, value);
        }

        public static readonly StyledProperty<bool> ShowCullingProperty =
            AvaloniaProperty.Register<DatObjectViewer, bool>(nameof(ShowCulling), true);

        public bool ShowCulling {
            get => GetValue(ShowCullingProperty);
            set => SetValue(ShowCullingProperty, value);
        }

        public DatObjectViewer() {
            InitializeComponent();
            InitializeBase3DView();
            _renderBackgroundColor = ExtractColor(ClearColor);
        }

        protected override void OnGlInit(GL gl, PixelSize canvasSize) {
            _gl = gl;
            var loggerFactory = WorldBuilder.App.Services?.GetService<ILoggerFactory>() ?? LoggerFactory.Create(builder => {
                builder.AddProvider(new ColorConsoleLoggerProvider());
                builder.SetMinimumLevel(LogLevel.Debug);
            });

            if (_renderDats != null) {
                var projectManager = WorldBuilder.App.Services?.GetService<ProjectManager>();
                var meshManagerService = projectManager?.GetProjectService<MeshManagerService>();
                var meshManager = meshManagerService?.GetMeshManager(Renderer!.GraphicsDevice, _renderDats);

                _scene = new SingleObjectScene(gl, Renderer!.GraphicsDevice, loggerFactory, _renderDats, meshManager);
                _scene.BackgroundColor = _renderBackgroundColor;
                _scene.IsAutoCamera = _renderIsAutoCamera;
                _scene.ShowWireframe = _renderShowWireframe;
                _scene.ShowCulling = _renderShowCulling;

                var settings = WorldBuilder.App.Services?.GetService<WorldBuilderSettings>();
                if (settings != null) {
                    _scene.EnableTransparencyPass = settings.Landscape.Rendering.EnableTransparencyPass;
                }

                _scene.Initialize();
                _scene.Resize(canvasSize.Width, canvasSize.Height);
                if (_renderFileId != 0) {
                    _scene.SetObject(_renderFileId, _renderIsSetup);
                }
            }
        }

        private static Vector4 ExtractColor(IBrush? brush) {
            if (brush is SolidColorBrush scb) {
                var color = scb.Color;
                return new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
            }
            return new Vector4(0.15f, 0.15f, 0.2f, 1.0f);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == FileIdProperty || change.Property == IsSetupProperty || change.Property == DatsProperty || change.Property == IsAutoCameraProperty) {
                // Sync values for render thread
                _renderFileId = FileId;
                _renderIsSetup = IsSetup;
                _renderDats = Dats;
                _renderIsAutoCamera = IsAutoCamera;

                if (Dispatcher.UIThread.CheckAccess()) {
                    UpdateObject();
                }
                else {
                    Dispatcher.UIThread.Post(UpdateObject);
                }
            }

            if (change.Property == IsAutoCameraProperty) {
                if (_scene != null) {
                    _scene.IsAutoCamera = _renderIsAutoCamera;
                }
            }

            if (change.Property == ShowWireframeProperty) {
                _renderShowWireframe = ShowWireframe;
                if (_scene != null) {
                    _scene.ShowWireframe = _renderShowWireframe;
                }
            }

            if (change.Property == ShowCullingProperty) {
                _renderShowCulling = ShowCulling;
                if (_scene != null) {
                    _scene.ShowCulling = _renderShowCulling;
                }
            }

            if (change.Property == ClearColorProperty) {
                _renderBackgroundColor = ExtractColor(ClearColor);
                if (_scene != null) {
                    _scene.BackgroundColor = _renderBackgroundColor;
                }
            }
        }

        private void UpdateObject() {
            if (_scene == null && _gl != null && _renderDats != null) {
                var loggerFactory = WorldBuilder.App.Services?.GetService<ILoggerFactory>() ?? LoggerFactory.Create(builder => {
                    builder.AddProvider(new ColorConsoleLoggerProvider());
                    builder.SetMinimumLevel(LogLevel.Debug);
                });

                var projectManager = WorldBuilder.App.Services?.GetService<ProjectManager>();
                var meshManagerService = projectManager?.GetProjectService<MeshManagerService>();
                var meshManager = meshManagerService?.GetMeshManager(Renderer!.GraphicsDevice, _renderDats);

                _scene = new SingleObjectScene(_gl, Renderer!.GraphicsDevice, loggerFactory, _renderDats, meshManager);

                var settings = WorldBuilder.App.Services?.GetService<WorldBuilderSettings>();
                if (settings != null) {
                    _scene.EnableTransparencyPass = settings.Landscape.Rendering.EnableTransparencyPass;
                }

                _scene.Initialize();
                _scene.Resize((int)Bounds.Width, (int)Bounds.Height);
            }

            if (_scene != null && _renderFileId != 0) {
                _ = _scene.LoadObjectAsync(_renderFileId, _renderIsSetup);
            }
        }

        protected override void OnGlRender(double frameTime) {
            if (_scene == null) return;
            _scene.Update((float)frameTime);
            _scene.Render();
        }

        protected override void OnGlResize(PixelSize canvasSize) {
            _scene?.Resize(canvasSize.Width, canvasSize.Height);
        }

        protected override void OnGlDestroy() {
            _scene?.Dispose();
            _scene = null;
        }

        protected override void OnGlKeyDown(KeyEventArgs e) {
            _scene?.HandleKeyDown(e.Key.ToString());
        }

        protected override void OnGlKeyUp(KeyEventArgs e) {
            _scene?.HandleKeyUp(e.Key.ToString());
        }

        protected override void OnGlPointerMoved(PointerEventArgs e, Vector2 mousePositionScaled) {
            var input = CreateInputEvent(e);
            _scene?.HandlePointerMoved(input.Position, input.Delta);
            _lastPointerPosition = mousePositionScaled;
        }

        private ViewportInputEvent CreateInputEvent(PointerEventArgs e) {
            var relativeTo = _viewport ?? this;
            var point = e.GetCurrentPoint(relativeTo);
            var size = new Vector2((float)relativeTo.Bounds.Width, (float)relativeTo.Bounds.Height) * InputScale;
            var pos = e.GetPosition(relativeTo);
            var posVec = new Vector2((float)pos.X, (float)pos.Y) * InputScale;
            var delta = posVec - _lastPointerPosition;

            return new ViewportInputEvent {
                Position = posVec,
                Delta = delta,
                ViewportSize = size,
                IsLeftDown = point.Properties.IsLeftButtonPressed,
                IsRightDown = point.Properties.IsRightButtonPressed,
                ShiftDown = (e.KeyModifiers & KeyModifiers.Shift) != 0,
                CtrlDown = (e.KeyModifiers & KeyModifiers.Control) != 0,
                AltDown = (e.KeyModifiers & KeyModifiers.Alt) != 0
            };
        }

        protected override void OnGlPointerWheelChanged(PointerWheelEventArgs e) {
            _scene?.HandlePointerWheelChanged((float)e.Delta.Y);
        }

        protected override void OnGlPointerPressed(PointerPressedEventArgs e) {
            // Focus this control to receive keyboard input
            this.Focus();

            var input = CreateInputEvent(e);
            int button = -1;
            var props = e.GetCurrentPoint(this).Properties;
            if (props.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed) button = 0;
            else if (props.PointerUpdateKind == PointerUpdateKind.RightButtonPressed) button = 1;
            else if (props.PointerUpdateKind == PointerUpdateKind.MiddleButtonPressed) button = 2;

            if (button != -1) {
                _scene?.HandlePointerPressed(button, input.Position);
            }
            _lastPointerPosition = input.Position;
        }

        protected override void OnGlPointerReleased(PointerReleasedEventArgs e) {
            var input = CreateInputEvent(e);
            int button = -1;
            var props = e.GetCurrentPoint(this).Properties;
            if (props.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased) button = 0;
            else if (props.PointerUpdateKind == PointerUpdateKind.RightButtonReleased) button = 1;
            else if (props.PointerUpdateKind == PointerUpdateKind.MiddleButtonReleased) button = 2;

            if (button != -1) {
                _scene?.HandlePointerReleased(button, input.Position);
            }
        }
    }
}
