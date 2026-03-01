using System;
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

    /// <summary>
    /// Gets or sets the camera yaw in degrees.
    /// </summary>
    public float Yaw {
        get => _yaw;
        set {
            _yaw = value % 360.0f;
            InvalidateMatrices();
            NotifyChanged();
        }
    }

    /// <summary>
    /// Gets or sets the camera pitch in degrees.
    /// </summary>
    public float Pitch {
        get => _pitch;
        set {
            _pitch = Math.Clamp(value, -89.9f, 89.9f);
            InvalidateMatrices();
            NotifyChanged();
        }
    }
    private float _nearPlane = 0.5f;
    private float _farPlane = 1000.0f;
    private float _moveSpeed = 10.0f;
    private float _lookSensitivity = 1.0f;
    private bool _isLooking;

    /// <summary>
    /// Gets or sets the near clipping plane distance.
    /// </summary>
    public float NearPlane {
        get => _nearPlane;
        set {
            _nearPlane = Math.Clamp(value, 0.001f, _farPlane - 0.1f);
            InvalidateMatrices();
        }
    }

    /// <summary>
    /// Event triggered when the movement speed changes.
    /// </summary>
    public event Action<float>? OnMoveSpeedChanged;

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

    // Turning state
    private bool _turnLeft;
    private bool _turnRight;
    private bool _turnUp;
    private bool _turnDown;
    private float _turnSpeed = 90.0f; // degrees per second
    private bool _shiftHeld;
    private float _speedMultiplier = 2.0f; // speed multiplier when shift is held

    /// <summary>
    /// Gets or sets the field of view in degrees.
    /// </summary>
    public override float FieldOfView {
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
        set {
            var newSpeed = Math.Max(0.1f, value);
            if (Math.Abs(_moveSpeed - newSpeed) > 0.001f) {
                _moveSpeed = newSpeed;
                OnMoveSpeedChanged?.Invoke(_moveSpeed);
            }
        }
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
    public override Vector3 Forward {
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

    /// <inheritdoc/>
    public override Quaternion Rotation {
        get {
            float yawRad = MathF.PI * _yaw / 180.0f;
            float pitchRad = MathF.PI * _pitch / 180.0f;

            // Yaw 0 is North (+Y), Pitch 0 is horizon
            var qYaw = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -yawRad);
            var qPitch = Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitchRad);

            return qYaw * qPitch;
        }
        set {
            // Extract Yaw and Pitch from Quaternion
            var forward = Vector3.Transform(new Vector3(0, 1, 0), value);
            _yaw = MathF.Atan2(forward.X, forward.Y) * 180f / MathF.PI;
            _pitch = MathF.Asin(Math.Clamp(forward.Z, -1f, 1f)) * 180f / MathF.PI;

            InvalidateMatrices();
            NotifyChanged();
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
    public override void LookAt(Vector3 target) {
        var diff = target - Position;
        if (diff == Vector3.Zero) return;
        var direction = Vector3.Normalize(diff);

        Yaw = MathF.Atan2(direction.X, direction.Y) * 180f / MathF.PI;
        Pitch = MathF.Asin(Math.Clamp(direction.Z, -1f, 1f)) * 180f / MathF.PI;
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
            float currentMoveSpeed = _shiftHeld ? _moveSpeed * _speedMultiplier : _moveSpeed;
            Position += movement * currentMoveSpeed * deltaTime;
        }

        // Handle keyboard turning
        float currentTurnSpeed = _shiftHeld ? _turnSpeed * _speedMultiplier : _turnSpeed;
        if (_turnLeft) {
            Yaw -= currentTurnSpeed * deltaTime;
        }
        if (_turnRight) {
            Yaw += currentTurnSpeed * deltaTime;
        }
        if (_turnUp) {
            Pitch += currentTurnSpeed * deltaTime;
        }
        if (_turnDown) {
            Pitch -= currentTurnSpeed * deltaTime;
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
            _yaw += delta.X * _lookSensitivity * 0.2f;
            _pitch -= delta.Y * _lookSensitivity * 0.2f;

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
            case "E":
                _moveUp = true;
                break;
            case "Q":
                _moveDown = true;
                break;
            case "LEFT":
                _turnLeft = true;
                break;
            case "RIGHT":
                _turnRight = true;
                break;
            case "UP":
                _turnUp = true;
                break;
            case "DOWN":
                _turnDown = true;
                break;
            case "LEFTSHIFT":
                _shiftHeld = true;
                break;
            case "R":
                Yaw = 0;
                Pitch = 0;
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
            case "E":
                _moveUp = false;
                break;
            case "Q":
                _moveDown = false;
                break;
            case "LEFT":
                _turnLeft = false;
                break;
            case "RIGHT":
                _turnRight = false;
                break;
            case "UP":
                _turnUp = false;
                break;
            case "DOWN":
                _turnDown = false;
                break;
            case "LEFTSHIFT":
                _shiftHeld = false;
                break;
        }
    }
}
