using System.Numerics;

namespace Chorizite.OpenGLSDLBackend;

/// <summary>
/// 2D orthographic camera with top-down view (Z is up).
/// Supports scroll wheel zoom and right-click drag panning.
/// </summary>
public class Camera2D : CameraBase {
    private const int RightMouseButton = 1;

    private float _zoom = 1.0f;
    private float _fov = 60.0f;
    private float _minZoom = 0.0001f;
    private float _maxZoom = 100.0f;
    private float _zoomSpeed = 0.1f;
    private bool _isPanning;
    private float _panSpeed = 1000.0f;
    private bool _shiftHeld;
    private float _speedMultiplier = 2.0f; // speed multiplier when shift is held

    // Movement state for keyboard panning
    private bool _panUp;
    private bool _panDown;
    private bool _panLeft;
    private bool _panRight;
    private bool _zoomIn;
    private bool _zoomOut;

    /// <summary>
    /// Gets or sets the zoom level (1.0 = default, higher = zoomed in).
    /// </summary>
    public float Zoom {
        get => _zoom;
        set {
            var newZoom = Math.Clamp(value, _minZoom, _maxZoom);
            if (Math.Abs(_zoom - newZoom) > float.Epsilon) {
                _zoom = newZoom;
                InvalidateMatrices();
                NotifyChanged();
            }
        }
    }

    /// <inheritdoc/>
    public override float FieldOfView {
        get => _fov;
        set {
            if (Math.Abs(_fov - value) > float.Epsilon) {
                _fov = value;
                InvalidateMatrices();
            }
        }
    }

    /// <summary>
    /// Gets or sets the minimum allowed zoom level.
    /// </summary>
    public float MinZoom {
        get => _minZoom;
        set => _minZoom = Math.Max(0.00001f, value);
    }

    /// <summary>
    /// Gets or sets the maximum allowed zoom level.
    /// </summary>
    public float MaxZoom {
        get => _maxZoom;
        set => _maxZoom = Math.Max(_minZoom, value);
    }

    /// <summary>
    /// Gets or sets the zoom speed multiplier.
    /// </summary>
    public float ZoomSpeed {
        get => _zoomSpeed;
        set => _zoomSpeed = Math.Max(0.01f, value);
    }

    /// <summary>
    /// Gets or sets the keyboard panning speed in units per second.
    /// </summary>
    public float PanSpeed {
        get => _panSpeed;
        set => _panSpeed = Math.Max(0.1f, value);
    }

    /// <summary>
    /// Creates a new 2D camera at the specified position.
    /// </summary>
    /// <param name="position">Initial camera position (X, Y in world space, Z ignored for view).</param>
    public Camera2D(Vector3 position = default) {
        _position = position;
    }

    /// <inheritdoc/>
    public override void LookAt(Vector3 target) {
        Position = new Vector3(target.X, target.Y, Position.Z);
    }

    /// <inheritdoc/>
    protected override void UpdateMatrices() {
        // View matrix: looking down -Z axis (top-down view, Z is up)
        // Camera is positioned at (X, Y, Z) looking at (X, Y, Z-1)
        var eye = new Vector3(_position.X, _position.Y, _position.Z);
        var target = new Vector3(_position.X, _position.Y, _position.Z - 1.0f);
        var up = new Vector3(0, 1, 0); // Y is "North" and "up" on screen
        _viewMatrix = Matrix4x4.CreateLookAt(eye, target, up);

        // Orthographic projection using world-space units
        // Base size of 10 units, scaled by zoom and aspect ratio
        float baseSize = 10.0f / _zoom;
        float aspectRatio = AspectRatio;
        float halfHeight = baseSize;
        float halfWidth = baseSize * aspectRatio;

        // Use a very large near/far plane to ensure we see terrain regardless of camera height
        // This also ensures raycasting works correctly from any height.
        _projectionMatrix = Matrix4x4.CreateOrthographic(halfWidth * 2, halfHeight * 2, -10000.0f, 10000.0f);
    }

    /// <inheritdoc/>
    public override void Update(float deltaTime) {
        // Handle keyboard panning
        float currentPanSpeed = _shiftHeld ? _panSpeed * _speedMultiplier : _panSpeed;
        
        // Use the same worldScale factor as mouse panning for consistent speed
        float worldScale = 20.0f / (_viewportHeight * _zoom);
        float scaledSpeed = currentPanSpeed * worldScale;
        
        if (_panUp) {
            _position.Y += scaledSpeed * deltaTime;
        }
        if (_panDown) {
            _position.Y -= scaledSpeed * deltaTime;
        }
        if (_panLeft) {
            _position.X -= scaledSpeed * deltaTime;
        }
        if (_panRight) {
            _position.X += scaledSpeed * deltaTime;
        }
        
        // Handle keyboard zooming
        float currentZoomSpeed = _shiftHeld ? _speedMultiplier : 1.0f;
        if (_zoomIn) {
            float zoomFactor = 1.0f + (currentZoomSpeed * deltaTime);
            Zoom *= zoomFactor;
        }
        if (_zoomOut) {
            float zoomFactor = 1.0f - (currentZoomSpeed * deltaTime);
            Zoom *= zoomFactor;
        }
        
        if (_panUp || _panDown || _panLeft || _panRight) {
            InvalidateMatrices();
        }
    }

    /// <inheritdoc/>
    public override void HandlePointerPressed(int button, Vector2 position) {
        if (button == RightMouseButton) {
            _isPanning = true;
        }
    }

    /// <inheritdoc/>
    public override void HandlePointerReleased(int button, Vector2 position) {
        if (button == RightMouseButton) {
            _isPanning = false;
        }
    }

    /// <inheritdoc/>
    public override void HandlePointerMoved(Vector2 position, Vector2 delta) {
        if (_isPanning) {
            // Convert screen delta to world delta (inverted and scaled by zoom and viewport size)
            // The world height is 20 units / zoom.
            float worldScale = 20.0f / (_viewportHeight * _zoom);
            _position.X -= delta.X * worldScale;
            _position.Y += delta.Y * worldScale;
            InvalidateMatrices();
        }
    }

    /// <inheritdoc/>
    public override void HandlePointerWheelChanged(float delta) {
        // Zoom in/out based on scroll direction
        float zoomFactor = 1.0f + (delta * _zoomSpeed);
        Zoom *= zoomFactor;
    }

    /// <inheritdoc/>
    public override void HandleKeyDown(string key) {
        switch (key.ToUpperInvariant()) {
            case "W":
                _panUp = true;
                break;
            case "S":
                _panDown = true;
                break;
            case "A":
                _panLeft = true;
                break;
            case "D":
                _panRight = true;
                break;
            case "UP":
                _zoomIn = true;
                break;
            case "DOWN":
                _zoomOut = true;
                break;
            case "LEFTSHIFT":
                _shiftHeld = true;
                break;
        }
    }

    /// <inheritdoc/>
    public override void HandleKeyUp(string key) {
        switch (key.ToUpperInvariant()) {
            case "W":
                _panUp = false;
                break;
            case "S":
                _panDown = false;
                break;
            case "A":
                _panLeft = false;
                break;
            case "D":
                _panRight = false;
                break;
            case "UP":
                _zoomIn = false;
                break;
            case "DOWN":
                _zoomOut = false;
                break;
            case "LEFTSHIFT":
                _shiftHeld = false;
                break;
        }
    }

    /// <inheritdoc/>
    public override Vector3 Forward => new Vector3(0, 0, -1);

    /// <inheritdoc/>
    public override Quaternion Rotation {
        get => Quaternion.Identity;
        set { /* Not supported in 2D */ }
    }
}