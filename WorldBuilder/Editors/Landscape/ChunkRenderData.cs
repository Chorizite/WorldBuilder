
// ===== Core Data Structures =====

using Chorizite.Core.Render.Vertex;
using System;

namespace WorldBuilder.Editors.Landscape {
    /// <summary>
    /// Immutable chunk rendering resources
    /// </summary>
    public class ChunkRenderData : IDisposable {
        public IVertexBuffer VertexBuffer { get; }
        public IIndexBuffer IndexBuffer { get; }
        public IVertexArray VertexArray { get; }
        public int VertexCount { get; }
        public int IndexCount { get; }

        public ChunkRenderData(IVertexBuffer vb, IIndexBuffer ib, IVertexArray va, int vertCount, int idxCount) {
            VertexBuffer = vb;
            IndexBuffer = ib;
            VertexArray = va;
            VertexCount = vertCount;
            IndexCount = idxCount;
        }

        public void Dispose() {
            VertexBuffer?.Dispose();
            IndexBuffer?.Dispose();
            VertexArray?.Dispose();
        }
    }
}