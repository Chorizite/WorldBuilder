using System.Numerics;

namespace Chorizite.OpenGLSDLBackend;

/// <summary>
/// 2D orthographic camera with top-down view (Z is up).
/// Supports scroll wheel zoom and right-click drag panning.
/// </summary>
public class Camera2D : CameraBase {
    private const int RightMouseButton = 1;

    private float _zoom = 1.0f;
    private float _minZoom = 0.1f;
    private float _maxZoom = 100.0f;
    private float _zoomSpeed = 0.1f;
    private bool _isPanning;

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
            }
        }
    }

    /// <summary>
    /// Gets or sets the minimum allowed zoom level.
    /// </summary>
    public float MinZoom {
        get => _minZoom;
        set => _minZoom = Math.Max(0.001f, value);
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
    /// Creates a new 2D camera at the specified position.
    /// </summary>
    /// <param name="position">Initial camera position (X, Y in world space, Z ignored for view).</param>
    public Camera2D(Vector3 position = default) {
        _position = position;
    }

    /// <inheritdoc/>
    protected override void UpdateMatrices() {
        // View matrix: looking down -Z axis (top-down view, Z is up)
        // Camera is positioned at (X, Y, height) looking at (X, Y, 0)
        float cameraHeight = 10.0f; // Fixed height above the XY plane
        var eye = new Vector3(_position.X, _position.Y, cameraHeight);
        var target = new Vector3(_position.X, _position.Y, 0);
        var up = new Vector3(0, 1, 0); // Y is "up" on screen
        _viewMatrix = Matrix4x4.CreateLookAt(eye, target, up);

        // Orthographic projection using world-space units
        // Base size of 10 units, scaled by zoom and aspect ratio
        float baseSize = 10.0f / _zoom;
        float aspectRatio = AspectRatio;
        float halfHeight = baseSize;
        float halfWidth = baseSize * aspectRatio;
        _projectionMatrix = Matrix4x4.CreateOrthographic(halfWidth * 2, halfHeight * 2, 0.1f, 1000.0f);
    }

    /// <inheritdoc/>
    public override void Update(float deltaTime) {
        // Camera2D doesn't have continuous updates, all controlled by input
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
        // No key controls for 2D camera
    }

    /// <inheritdoc/>
    public override void HandleKeyUp(string key) {
        // No key controls for 2D camera
    }
}
