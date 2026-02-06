using System.Numerics;

namespace Chorizite.OpenGLSDLBackend;

/// <summary>
/// 3D perspective camera with WASD movement and right-click mouselook.
/// </summary>
public class Camera3D : CameraBase {
    private const int RightMouseButton = 1;

    private float _yaw; // Rotation around Y axis (left/right)
    private float _pitch; // Rotation around X axis (up/down)
    private float _fov = 60.0f;
    private float _nearPlane = 0.1f;
    private float _farPlane = 1000.0f;
    private float _moveSpeed = 10.0f;
    private float _lookSensitivity = 0.3f;
    private bool _isLooking;

    /// <summary>
    /// Gets or sets the far clipping plane distance.
    /// </summary>
    public float FarPlane {
        get => _farPlane;
        set {
            _farPlane = Math.Max(_nearPlane + 0.1f, value);
            InvalidateMatrices();
        }
    }

    // Movement state
    private bool _moveForward;
    private bool _moveBackward;
    private bool _moveLeft;
    private bool _moveRight;
    private bool _moveUp;
    private bool _moveDown;

    /// <summary>
    /// Gets or sets the field of view in degrees.
    /// </summary>
    public float FieldOfView {
        get => _fov;
        set {
            _fov = Math.Clamp(value, 1.0f, 179.0f);
            InvalidateMatrices();
        }
    }

    /// <summary>
    /// Gets or sets the movement speed in units per second.
    /// </summary>
    public float MoveSpeed {
        get => _moveSpeed;
        set => _moveSpeed = Math.Max(0.1f, value);
    }

    /// <summary>
    /// Gets or sets the mouse look sensitivity.
    /// </summary>
    public float LookSensitivity {
        get => _lookSensitivity;
        set => _lookSensitivity = Math.Max(0.01f, value);
    }

    /// <summary>
    /// Gets the forward direction vector.
    /// </summary>
    public Vector3 Forward {
        get {
            float yawRad = MathF.PI * _yaw / 180.0f;
            float pitchRad = MathF.PI * _pitch / 180.0f;

            // Z-up coordinate system:
            // Yaw 0 -> +Y (North)
            // Pitch 0 -> Horizon (XY plane)
            // Pitch +90 -> +Z (Up)
            return Vector3.Normalize(new Vector3(
                MathF.Cos(pitchRad) * MathF.Sin(yawRad),
                MathF.Cos(pitchRad) * MathF.Cos(yawRad),
                MathF.Sin(pitchRad)
            ));
        }
    }

    /// <summary>
    /// Gets the right direction vector.
    /// </summary>
    public Vector3 Right {
        get {
            // Right is perpendicular to Forward and World Up (Z)
            return Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitZ));
        }
    }

    /// <summary>
    /// Gets the up direction vector (camera up, not world up).
    /// </summary>
    public Vector3 Up => Vector3.Normalize(Vector3.Cross(Right, Forward));

    /// <summary>
    /// Creates a new 3D camera at the specified position.
    /// </summary>
    /// <param name="position">Initial camera position.</param>
    /// <param name="yaw">Initial yaw angle in degrees.</param>
    /// <param name="pitch">Initial pitch angle in degrees.</param>
    public Camera3D(Vector3 position = default, float yaw = 0, float pitch = 0) {
        _position = position;
        _yaw = yaw;
        _pitch = pitch;
    }

    /// <inheritdoc/>
    protected override void UpdateMatrices() {
        // Calculate target point
        var target = _position + Forward;
        _viewMatrix = Matrix4x4.CreateLookAt(_position, target, Up);

        // Perspective projection
        float fovRad = MathF.PI * _fov / 180.0f;
        _projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(fovRad, AspectRatio, _nearPlane, _farPlane);
    }

    /// <inheritdoc/>
    public override void Update(float deltaTime) {
        var movement = Vector3.Zero;

        if (_moveForward) movement += Forward;
        if (_moveBackward) movement -= Forward;
        if (_moveRight) movement += Right;
        if (_moveLeft) movement -= Right;
        if (_moveUp) movement += Up;
        if (_moveDown) movement -= Up;

        if (movement != Vector3.Zero) {
            movement = Vector3.Normalize(movement);
            Position += movement * _moveSpeed * deltaTime;
        }
    }

    /// <inheritdoc/>
    public override void HandlePointerPressed(int button, Vector2 position) {
        if (button == RightMouseButton) {
            _isLooking = true;
        }
    }

    /// <inheritdoc/>
    public override void HandlePointerReleased(int button, Vector2 position) {
        if (button == RightMouseButton) {
            _isLooking = false;
        }
    }

    /// <inheritdoc/>
    public override void HandlePointerMoved(Vector2 position, Vector2 delta) {
        if (_isLooking) {
            _yaw += delta.X * _lookSensitivity;
            _pitch -= delta.Y * _lookSensitivity;

            // Clamp pitch to prevent flipping
            _pitch = Math.Clamp(_pitch, -89.0f, 89.0f);

            // Normalize yaw
            _yaw %= 360.0f;

            InvalidateMatrices();
        }
    }

    /// <inheritdoc/>
    public override void HandlePointerWheelChanged(float delta) {
        // Change camera speed based on scroll wheel
        MoveSpeed += delta * MoveSpeed * 0.1f;
    }

    /// <inheritdoc/>
    public override void HandleKeyDown(string key) {
        switch (key.ToUpperInvariant()) {
            case "W":
                _moveForward = true;
                break;
            case "S":
                _moveBackward = true;
                break;
            case "A":
                _moveLeft = true;
                break;
            case "D":
                _moveRight = true;
                break;
            case "SPACE":
            case "E":
                _moveUp = true;
                break;
            case "LEFTSHIFT":
            case "Q":
                _moveDown = true;
                break;
        }
    }

    /// <inheritdoc/>
    public override void HandleKeyUp(string key) {
        switch (key.ToUpperInvariant()) {
            case "W":
                _moveForward = false;
                break;
            case "S":
                _moveBackward = false;
                break;
            case "A":
                _moveLeft = false;
                break;
            case "D":
                _moveRight = false;
                break;
            case "SPACE":
            case "E":
                _moveUp = false;
                break;
            case "LEFTSHIFT":
            case "Q":
                _moveDown = false;
                break;
        }
    }
}
