using System.Numerics;

using WorldBuilder.Shared.Models;

namespace Chorizite.OpenGLSDLBackend;

/// <summary>
/// Interface for camera implementations providing view and projection matrices.
/// </summary>
public interface ICamera : WorldBuilder.Shared.Models.ICamera {
    /// <summary>
    /// Gets the view matrix for transforming world coordinates to camera space.
    /// </summary>
    new Matrix4x4 ViewMatrix { get; }

    /// <summary>
    /// Gets the projection matrix for transforming camera space to clip space.
    /// </summary>
    new Matrix4x4 ProjectionMatrix { get; }

    /// <summary>
    /// Gets the combined view-projection matrix.
    /// </summary>
    Matrix4x4 ViewProjectionMatrix { get; }

    /// <summary>
    /// Gets or sets the camera position in world space.
    /// </summary>
    new Vector3 Position { get; set; }

    /// <summary>
    /// Updates the camera state based on elapsed time.
    /// </summary>
    /// <param name="deltaTime">Time elapsed since last update in seconds.</param>
    void Update(float deltaTime);

    /// <summary>
    /// Handles viewport resize.
    /// </summary>
    /// <param name="width">New viewport width in pixels.</param>
    /// <param name="height">New viewport height in pixels.</param>
    void Resize(int width, int height);

    /// <summary>
    /// Handles pointer press events.
    /// </summary>
    /// <param name="button">The mouse button pressed (0=left, 1=right, 2=middle).</param>
    /// <param name="position">The pointer position.</param>
    void HandlePointerPressed(int button, Vector2 position);

    /// <summary>
    /// Handles pointer release events.
    /// </summary>
    /// <param name="button">The mouse button released.</param>
    /// <param name="position">The pointer position.</param>
    void HandlePointerReleased(int button, Vector2 position);

    /// <summary>
    /// Handles pointer move events.
    /// </summary>
    /// <param name="position">The current pointer position.</param>
    /// <param name="delta">The movement delta since last frame.</param>
    void HandlePointerMoved(Vector2 position, Vector2 delta);

    /// <summary>
    /// Handles mouse wheel scroll events.
    /// </summary>
    /// <param name="delta">The scroll delta (positive = scroll up).</param>
    void HandlePointerWheelChanged(float delta);

    /// <summary>
    /// Handles key press events.
    /// </summary>
    /// <param name="key">The key that was pressed.</param>
    void HandleKeyDown(string key);

    /// <summary>
    /// Handles key release events.
    /// </summary>
    /// <param name="key">The key that was released.</param>
    void HandleKeyUp(string key);
}