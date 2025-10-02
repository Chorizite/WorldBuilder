using Chorizite.Core.Lib;
using Chorizite.Core.Render.Vertex;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorldBuilder.Services {
    public class TerrainMeshData {
        public VertexLandscape[] Vertices { get; set; }
        public uint[] Indices { get; set; }
        public int VertexCount { get; set; }
        public int IndexCount { get; set; }
        public BoundingBox Bounds { get; set; }

        // Chunk identification
        public uint LandblockX { get; set; }
        public uint LandblockY { get; set; }
        public uint ChunkSizeInLandblocks { get; set; }

        public TerrainMeshData() {

        }

        public Span<VertexLandscape> GetVertexSpan() => Vertices;
        public Span<uint> GetIndexSpan() => Indices;
    }
}
