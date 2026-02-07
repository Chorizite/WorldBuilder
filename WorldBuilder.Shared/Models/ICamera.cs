using System.Numerics;

namespace WorldBuilder.Shared.Models
{
    public interface ICamera
    {
        Vector3 Position { get; }
        Matrix4x4 ViewMatrix { get; }
        Matrix4x4 ProjectionMatrix { get; }
    }
}
