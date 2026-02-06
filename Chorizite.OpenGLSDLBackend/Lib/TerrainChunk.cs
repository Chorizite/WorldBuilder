using Chorizite.Core.Lib;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;

namespace Chorizite.OpenGLSDLBackend.Lib {
    /// <summary>
    /// Represents a renderable chunk of terrain logic and GPU resources.
    /// </summary>
    public class TerrainChunk : IDisposable {
        private bool _disposed;

        public uint ChunkX { get; private set; }
        public uint ChunkY { get; private set; }

        // These will be used by the geometry generator
        public uint LandblockStartX => ChunkX * 8;
        public uint LandblockStartY => ChunkY * 8;
        public uint ActualLandblockCountX { get; set; } = 8;
        public uint ActualLandblockCountY { get; set; } = 8;

        public BoundingBox Bounds { get; set; }
        public bool IsGenerated { get; set; }

        // GPU Resources
        public uint VAO { get; set; }
        public uint VBO { get; set; }
        public uint EBO { get; set; }
        public int IndexCount { get; set; }

        // Temporary data for upload
        public Memory<VertexLandscape> GeneratedVertices { get; set; }
        public Memory<uint> GeneratedIndices { get; set; }

        public TerrainChunk(uint x, uint y) {
            ChunkX = x;
            ChunkY = y;
        }

        public ulong GetChunkId() => (ulong)ChunkX << 32 | ChunkY;

        public void Dispose() {
            if (_disposed) return;
            // Note: GL resources should ideally be deleted on the main thread/GL context
            // The manager should handle calling this or queueing deletion
            _disposed = true;
        }
    }
}
