using Chorizite.Core.Render.Vertex;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldBuilder.Tools.Landscape {
    public class TerrainChunk : IDisposable {
        public BoundingBox BoundingBox { get; set; }
        public required uint LandblockX { get; set; }
        public required uint LandblockY { get; set; }
        public bool IsGenerated { get; set; }
        public bool IsVisible { get; set; }
        public IVertexBuffer VertexBuffer { get; set; }
        public IIndexBuffer IndexBuffer { get; set; }
        public IVertexArray VertexArray { get; set; }

        // Cache vertex/index counts for rendering
        public int VertexCount { get; set; }
        public int IndexCount { get; set; }

        /// <summary>
        /// Marks this chunk as needing buffer regeneration
        /// </summary>
        public bool NeedsBufferUpdate { get; set; } = false;

        /// <summary>
        /// Track which landblocks in this chunk have been modified and need updates
        /// </summary>
        public HashSet<uint> ModifiedLandblocks { get; set; } = new HashSet<uint>();

        /// <summary>
        /// Marks specific landblocks as modified
        /// </summary>
        /// <param name="landblockIds">The landblock IDs that have been modified</param>
        public void MarkLandblocksModified(params uint[] landblockIds) {
            foreach (var id in landblockIds) {
                ModifiedLandblocks.Add(id);
            }
        }

        /// <summary>
        /// Clears the modified landblocks set after updates are applied
        /// </summary>
        public void ClearModifiedLandblocks() {
            ModifiedLandblocks.Clear();
        }

        /// <summary>
        /// Gets the landblock index within this chunk
        /// </summary>
        /// <param name="landblockId">The landblock ID</param>
        /// <param name="chunkSizeInLandblocks">Size of chunk in landblocks</param>
        /// <returns>Local index within the chunk, or -1 if not in this chunk</returns>
        public int GetLandblockIndex(uint landblockId, uint chunkSizeInLandblocks) {
            var landblockX = landblockId >> 8;
            var landblockY = landblockId & 0xFF;

            var chunkX = LandblockX / chunkSizeInLandblocks;
            var chunkY = LandblockY / chunkSizeInLandblocks;

            var localX = landblockX - (chunkX * chunkSizeInLandblocks);
            var localY = landblockY - (chunkY * chunkSizeInLandblocks);

            if (localX >= chunkSizeInLandblocks || localY >= chunkSizeInLandblocks) {
                return -1; // Not in this chunk
            }

            return (int)(localY * chunkSizeInLandblocks + localX);
        }

        /// <summary>
        /// Forces disposal and regeneration of graphics resources
        /// </summary>
        public void ForceRegenerateBuffers() {
            Dispose();
            NeedsBufferUpdate = true;
            ModifiedLandblocks.Clear();
        }

        public void Dispose() {
            VertexBuffer?.Dispose();
            IndexBuffer?.Dispose();
            VertexArray?.Dispose();
            VertexBuffer = null;
            IndexBuffer = null;
            VertexArray = null;
        }
    }

    public struct TerrainVertex {
        public Vector3 Position { get; set; }
        public TerrainVertex(Vector3 position) {
            Position = position;
        }
    }

    public struct Ray {
        public Vector3 Origin { get; set; }
        public Vector3 Direction { get; set; }
        public Ray(Vector3 origin, Vector3 direction) {
            Origin = origin;
            Direction = Vector3.Normalize(direction);
        }
    }
}