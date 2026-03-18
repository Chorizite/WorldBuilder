using System.Numerics;
using Chorizite.OpenGLSDLBackend.Lib;
using DatReaderWriter.Types;

namespace Chorizite.OpenGLSDLBackend.Lib {
    public class ActiveParticleEmitter {
        public ParticleEmitterRenderer Renderer { get; }
        public uint PartIndex { get; }
        public Matrix4x4 LocalOffset { get; }
        public SceneryInstance? ParentInstance { get; }

        public ActiveParticleEmitter(ParticleEmitterRenderer renderer, uint partIndex, Matrix4x4 localOffset, SceneryInstance? parentInstance = null) {
            Renderer = renderer;
            PartIndex = partIndex;
            LocalOffset = localOffset;
            ParentInstance = parentInstance;
        }

        public void Update(float deltaTime, Matrix4x4 parentTransform) {
            var combinedTransform = LocalOffset * parentTransform;
            if (ParentInstance.HasValue) {
                // Find part transform if applicable
                // For GameScene, we'd need to know the part transforms of the model.
                // For now, let's just use the root transform.
                combinedTransform = LocalOffset * ParentInstance.Value.Transform;
            }
            Renderer.ParentTransform = combinedTransform;
            Renderer.Update(deltaTime);
        }

        public void Render(Matrix4x4 viewProjection, Vector3 cameraUp, Vector3 cameraRight) {
            Renderer.Render(viewProjection, cameraUp, cameraRight);
        }
    }
}
