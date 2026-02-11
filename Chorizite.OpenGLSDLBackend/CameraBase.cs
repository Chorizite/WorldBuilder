using System.Numerics;

namespace Chorizite.OpenGLSDLBackend;

/// <summary>
/// Abstract base class for camera implementations with shared functionality.
/// </summary>
public abstract class CameraBase : ICamera {
    protected Vector3 _position;
    protected int _viewportWidth = 1;
    protected int _viewportHeight = 1;
    protected bool _matricesDirty = true;
    protected Matrix4x4 _viewMatrix = Matrix4x4.Identity;
    protected Matrix4x4 _projectionMatrix = Matrix4x4.Identity;
    protected Matrix4x4 _viewProjectionMatrix = Matrix4x4.Identity;

    /// <inheritdoc/>
    public Vector3 Position {
        get => _position;
        set {
            if (_position != value) {
                _position = value;
                _matricesDirty = true;
            }
        }
    }

    /// <inheritdoc/>
    public Matrix4x4 ViewMatrix {
        get {
            EnsureMatricesUpdated();
            return _viewMatrix;
        }
    }

    /// <inheritdoc/>
    public Matrix4x4 ProjectionMatrix {
        get {
            EnsureMatricesUpdated();
            return _projectionMatrix;
        }
    }

    /// <inheritdoc/>
    public Matrix4x4 ViewProjectionMatrix {
        get {
            EnsureMatricesUpdated();
            return _viewProjectionMatrix;
        }
    }

    /// <summary>
    /// Gets the viewport aspect ratio.
    /// </summary>
    protected float AspectRatio => _viewportWidth / (float)_viewportHeight;

    /// <inheritdoc/>
    public virtual void Resize(int width, int height) {
        if (width > 0 && height > 0) {
            _viewportWidth = width;
            _viewportHeight = height;
            _matricesDirty = true;
        }
    }

    /// <inheritdoc/>
    public abstract void Update(float deltaTime);

    /// <inheritdoc/>
    public abstract void HandlePointerPressed(int button, Vector2 position);

    /// <inheritdoc/>
    public abstract void HandlePointerReleased(int button, Vector2 position);

    /// <inheritdoc/>
    public abstract void HandlePointerMoved(Vector2 position, Vector2 delta);

    /// <inheritdoc/>
    public abstract void HandlePointerWheelChanged(float delta);

    /// <inheritdoc/>
    public abstract void HandleKeyDown(string key);

    /// <inheritdoc/>
    public abstract void HandleKeyUp(string key);

    /// <summary>
    /// Ensures view and projection matrices are up to date.
    /// </summary>
    protected void EnsureMatricesUpdated() {
        if (_matricesDirty) {
            UpdateMatrices();
            _viewProjectionMatrix = _viewMatrix * _projectionMatrix;
            _matricesDirty = false;
        }
    }

    /// <summary>
    /// Updates the view and projection matrices. Called when matrices are dirty.
    /// </summary>
    protected abstract void UpdateMatrices();

    /// <summary>
    /// Marks matrices as needing recalculation.
    /// </summary>
    protected void InvalidateMatrices() {
        _matricesDirty = true;
    }
}