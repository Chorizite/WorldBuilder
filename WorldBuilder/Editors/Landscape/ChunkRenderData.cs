using Chorizite.Core.Render;
using Chorizite.Core.Render.Vertex;
using System;

namespace WorldBuilder.Editors.Landscape {
    /// <summary>
    /// GPU resources for a single landblock within a chunk's buffer
    /// </summary>
    public class LandblockRenderData {
        public uint LandblockId { get; set; }
        public int VertexOffset { get; set; }
        public int IndexOffset { get; set; }
        public int VertexCount { get; set; }
        public int IndexCount { get; set; }

        /// <summary>
        /// Whether this landblock has valid geometry
        /// </summary>
        public bool HasGeometry => VertexCount > 0 && IndexCount > 0;
    }

    /// <summary>
    /// GPU resources for an entire chunk with per-landblock tracking
    /// </summary>
    public class ChunkRenderData : IDisposable {
        public IVertexBuffer VertexBuffer { get; }
        public IIndexBuffer IndexBuffer { get; }
        public IVertexArray ArrayBuffer { get; }
        public int TotalVertexCount { get; set; }
        public int TotalIndexCount { get; set; }

        /// <summary>
        /// Render data for each landblock in this chunk (key = landblock ID)
        /// </summary>
        public readonly System.Collections.Generic.Dictionary<uint, LandblockRenderData> LandblockData = new();

        public ChunkRenderData(
            IVertexBuffer vertexBuffer,
            IIndexBuffer indexBuffer,
            IVertexArray arrayBuffer,
            int totalVertexCount,
            int totalIndexCount) {
            VertexBuffer = vertexBuffer ?? throw new ArgumentNullException(nameof(vertexBuffer));
            IndexBuffer = indexBuffer ?? throw new ArgumentNullException(nameof(indexBuffer));
            ArrayBuffer = arrayBuffer ?? throw new ArgumentNullException(nameof(arrayBuffer));
            TotalVertexCount = totalVertexCount;
            TotalIndexCount = totalIndexCount;
        }

        public void Dispose() {
            ArrayBuffer?.Dispose();
            VertexBuffer?.Dispose();
            IndexBuffer?.Dispose();
        }
    }
}