using System.Numerics;

namespace WorldBuilder.Shared.Models {
    /// <summary>
    /// Represents a camera in the 3D world.
    /// </summary>
    public interface ICamera {
        /// <summary>The position of the camera in world space.</summary>
        Vector3 Position { get; }
        /// <summary>The view matrix of the camera.</summary>
        Matrix4x4 ViewMatrix { get; }
        /// <summary>The projection matrix of the camera.</summary>
        Matrix4x4 ProjectionMatrix { get; }
        /// <summary>The view-projection matrix of the camera.</summary>
        Matrix4x4 ViewProjectionMatrix { get; }
        /// <summary>The forward direction vector of the camera.</summary>
        Vector3 Forward { get; }
    }
}