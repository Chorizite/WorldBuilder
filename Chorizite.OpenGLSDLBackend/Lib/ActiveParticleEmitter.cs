using System.Numerics;
using Chorizite.OpenGLSDLBackend.Lib;
using DatReaderWriter.Types;
using WorldBuilder.Shared.Models;

namespace Chorizite.OpenGLSDLBackend.Lib {
    public class ActiveParticleEmitter {
        public ParticleEmitterRenderer Renderer { get; }
        public uint PartIndex { get; }
        public Matrix4x4 LocalOffset { get; }
        
        // Store reference info instead of struct copy
        public ObjectLandblock? ParentLandblock { get; set; }
        public ObjectId? ParentInstanceId { get; set; }

        public ActiveParticleEmitter(ParticleEmitterRenderer renderer, uint partIndex, Matrix4x4 localOffset, ObjectLandblock? parentLandblock = null, ObjectId? parentInstanceId = null) {
            Renderer = renderer;
            PartIndex = partIndex;
            LocalOffset = localOffset;
            ParentLandblock = parentLandblock;
            ParentInstanceId = parentInstanceId;
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
