using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using Chorizite.OpenGLSDLBackend;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using System;
using System.Numerics;
using WorldBuilder.Lib;
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

        public DatObjectViewer() {
            InitializeComponent();
            InitializeBase3DView();
        }

        protected override void OnGlInit(GL gl, PixelSize canvasSize) {
            _gl = gl;
            var loggerFactory = WorldBuilder.App.Services?.GetService<ILoggerFactory>();
            var log = loggerFactory?.CreateLogger("DatObjectViewer") ?? new ColorConsoleLogger("DatObjectViewer", () => new ColorConsoleLoggerConfiguration());
            
            if (_renderDats != null) {
                _scene = new SingleObjectScene(gl, Renderer!.GraphicsDevice, log, _renderDats);
                _scene.Initialize();
                _scene.Resize(canvasSize.Width, canvasSize.Height);
                if (_renderFileId != 0) {
                    _scene.SetObject(_renderFileId, _renderIsSetup);
                }
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == FileIdProperty || change.Property == IsSetupProperty || change.Property == DatsProperty) {
                // Sync values for render thread
                _renderFileId = FileId;
                _renderIsSetup = IsSetup;
                _renderDats = Dats;

                if (Dispatcher.UIThread.CheckAccess()) {
                    UpdateObject();
                } else {
                    Dispatcher.UIThread.Post(UpdateObject);
                }
            }
        }

        private void UpdateObject() {
            if (_scene == null && _gl != null && _renderDats != null) {
                 var loggerFactory = WorldBuilder.App.Services?.GetService<ILoggerFactory>();
                 var log = loggerFactory?.CreateLogger("DatObjectViewer") ?? new ColorConsoleLogger("DatObjectViewer", () => new ColorConsoleLoggerConfiguration());
                 _scene = new SingleObjectScene(_gl, Renderer!.GraphicsDevice, log, _renderDats);
                 _scene.Initialize();
                 _scene.Resize((int)Bounds.Width, (int)Bounds.Height);
            }

            if (_scene != null && _renderFileId != 0) {
                _scene.SetObject(_renderFileId, _renderIsSetup);
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

        protected override void OnGlKeyDown(KeyEventArgs e) { }
        protected override void OnGlKeyUp(KeyEventArgs e) { }
        
        protected override void OnGlPointerMoved(PointerEventArgs e, Vector2 mousePositionScaled) {
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

        protected override void OnGlPointerWheelChanged(PointerWheelEventArgs e) { }
        
        protected override void OnGlPointerPressed(PointerPressedEventArgs e) {
             _lastPointerPosition = CreateInputEvent(e).Position;
        }
        
        protected override void OnGlPointerReleased(PointerReleasedEventArgs e) { }
    }
}
