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
            Renderer.ParentTransform = parentTransform;
            Renderer.LocalOffset = LocalOffset;
            Renderer.Update(deltaTime);
        }

        public void Render(ParticleBatcher batcher) {
            Renderer.Render(batcher);
        }
    }
}
