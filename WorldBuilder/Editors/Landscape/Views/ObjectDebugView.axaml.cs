using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Chorizite.OpenGLSDLBackend;
using Silk.NET.OpenGL;
using System;
using System.Numerics;
using System.Runtime.Serialization;
using WorldBuilder.Editors.Landscape.ViewModels;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Views;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace WorldBuilder.Editors.Landscape.Views;

public partial class ObjectDebugView : Base3DView {
    private ObjectDebugViewModel? _vm;
    private OpenGLRenderer? _renderer;
    private GL _gl;
    private IDatReaderWriter _dats;

    public PixelSize CanvasSize { get; private set; }

    private StaticObjectManager _staticObjectManager;

    // Mouse state for rotation (drag to rotate around)
    private PointerPoint? _lastPointerPoint;
    private bool _isRotating = false;

    public ObjectDebugView() {
        InitializeComponent();
        InitializeBase3DView();
        _vm = new ObjectDebugViewModel();
        DataContext = _vm;

    }

    protected override void OnGlDestroy() {
        _vm?.Dispose();
    }

    protected override void OnGlInit(GL gl, PixelSize canvasSize) {
        _dats = ProjectManager.Instance.CurrentProject.DatReaderWriter;
        _gl = gl;
        CanvasSize = canvasSize;

        _staticObjectManager = new StaticObjectManager(Renderer, _dats);
        _vm.Init(Renderer, _dats, _staticObjectManager);
    }

    protected override void OnGlKeyDown(KeyEventArgs e) {

    }

    protected override void OnGlKeyUp(KeyEventArgs e) {

    }

    protected override void OnGlPointerMoved(PointerEventArgs e, Vector2 mousePositionScaled) {
        var point = e.GetCurrentPoint(this);
        if (_isRotating && point.Properties.IsLeftButtonPressed) {
            if (_lastPointerPoint.HasValue) {
                var deltaX = (float)(point.Position.X - _lastPointerPoint.Value.Position.X);
                var deltaY = (float)(point.Position.Y - _lastPointerPoint.Value.Position.Y);
                _vm?.RotateAround(deltaY * 0.5f, -deltaX * 0.5f); // Adjust sensitivity; Y for yaw (horizontal), X for pitch (vertical)
                InvalidateVisual(); // Trigger re-render
            }
            _lastPointerPoint = point;
        }
    }

    protected override void OnGlPointerPressed(PointerPressedEventArgs e) {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed) {
            _isRotating = true;
            _lastPointerPoint = point;
            e.Pointer.Capture(this); // Capture mouse for drag
        }
    }

    protected override void OnGlPointerReleased(PointerReleasedEventArgs e) {
        if (_isRotating) {
            _isRotating = false;
            _lastPointerPoint = null;
            e.Pointer.Capture(null); // Release capture
        }
    }

    protected override void OnGlPointerWheelChanged(PointerWheelEventArgs e) {
        var delta = (float)e.Delta.Y; // Positive = scroll up (zoom in), negative = scroll down (zoom out)
        _vm?.Zoom(-delta); // Invert if needed: negative delta to zoom out on scroll down
        InvalidateVisual(); // Trigger re-render
    }

    protected override void OnGlRender(double frameTime) {
        _vm?.Render(CanvasSize);
    }

    protected override void OnGlResize(PixelSize canvasSize) {
        CanvasSize = canvasSize;
    }
}