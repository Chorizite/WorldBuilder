using System.Numerics;
using WorldBuilder.Shared.Models;
using BackendCamera = Chorizite.OpenGLSDLBackend.ICamera;

namespace WorldBuilder.Modules.Landscape {
    public class CameraAdapter : ICamera {
        private readonly BackendCamera _backendCamera;

        public CameraAdapter(BackendCamera backendCamera) {
            _backendCamera = backendCamera;
        }

        public Vector3 Position => _backendCamera.Position;
        public Matrix4x4 ViewMatrix => _backendCamera.ViewMatrix;
        public Matrix4x4 ProjectionMatrix => _backendCamera.ProjectionMatrix;
    }
}