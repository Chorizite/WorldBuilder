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

        /// <summary>
        /// Stores the starting vertex index in the VBO for each of the 64 landblocks (8x8).
        /// Value is -1 if the landblock has no geometry (e.g. out of bounds).
        /// </summary>
        public int[] LandblockVertexOffsets { get; } = new int[64];

        /// <summary>
        /// Tracks which landblocks are dirty and need partial updates.
        /// Index is (ly * 8 + lx).
        /// </summary>
        private readonly bool[] _dirtyLandblocks = new bool[64];
        private readonly object _dirtyLock = new();

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
            // Initialize offsets to -1
            Array.Fill(LandblockVertexOffsets, -1);
        }

        public ulong GetChunkId() => (ulong)ChunkX << 32 | ChunkY;

        public void MarkDirty(int localLx, int localLy) {
            if (localLx < 0 || localLx >= 8 || localLy < 0 || localLy >= 8) return;
            lock (_dirtyLock) {
                _dirtyLandblocks[localLy * 8 + localLx] = true;
            }
        }

        public void MarkAllDirty() {
            lock (_dirtyLock) {
                for (int i = 0; i < 64; i++) {
                    _dirtyLandblocks[i] = true;
                }
            }
        }

        public bool TryGetNextDirty(out int localLx, out int localLy) {
            lock (_dirtyLock) {
                for (int i = 0; i < 64; i++) {
                    if (_dirtyLandblocks[i]) {
                        _dirtyLandblocks[i] = false;
                        localLx = i % 8;
                        localLy = i / 8;
                        return true;
                    }
                }
            }
            localLx = -1;
            localLy = -1;
            return false;
        }

        public bool HasDirtyBlocks() {
            lock (_dirtyLock) {
                for (int i = 0; i < 64; i++) {
                    if (_dirtyLandblocks[i]) return true;
                }
            }
            return false;
        }

        public void Dispose() {
            if (_disposed) return;
            // Note: GL resources should ideally be deleted on the main thread/GL context
            // The manager should handle calling this or queueing deletion
            _disposed = true;
        }
    }
}
