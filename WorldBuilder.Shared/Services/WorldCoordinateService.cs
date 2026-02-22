using System.Collections.Generic;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Services;

/// <inheritdoc/>
public class WorldCoordinateService : IWorldCoordinateService {
    public const int LandblocksPerChunk = 8;
    public const int ChunkVertexStride = 65; // (8 blocks * 8 vertices/block) + 1

    /// <inheritdoc/>
    public (ushort chunkId, ushort localIndex) GetLocalVertexIndex(uint globalVertexIndex, ITerrainInfo region) {
        if (region == null || region.MapWidthInVertices == 0) return (0, 0);

        int mapWidth = region.MapWidthInVertices;
        int globalY = (int)(globalVertexIndex / (uint)mapWidth);
        int globalX = (int)(globalVertexIndex % (uint)mapWidth);

        int chunkX = globalX / (ChunkVertexStride - 1);
        int chunkY = globalY / (ChunkVertexStride - 1);

        int localX = globalX % (ChunkVertexStride - 1);
        int localY = globalY % (ChunkVertexStride - 1);

        ushort chunkId = GetChunkId((uint)chunkX, (uint)chunkY);
        ushort localIndex = (ushort)(localY * ChunkVertexStride + localX);
        return (chunkId, localIndex);
    }

    /// <inheritdoc/>
    public uint GetGlobalVertexIndex(ushort chunkId, ushort localIndex, ITerrainInfo region) {
        if (region == null) return 0;

        var (chunkX, chunkY) = GetChunkCoords(chunkId);
        int localY = localIndex / ChunkVertexStride;
        int localX = localIndex % ChunkVertexStride;

        int globalX = (int)chunkX * (ChunkVertexStride - 1) + localX;
        int globalY = (int)chunkY * (ChunkVertexStride - 1) + localY;
        return (uint)(globalY * region.MapWidthInVertices + globalX);
    }

    /// <inheritdoc/>
    public IEnumerable<(ushort chunkId, ushort localIndex)> GetAffectedChunksWithBoundaries(uint globalVertexIndex, ITerrainInfo region) {
        var (primaryChunkId, primaryLocalIndex) = GetLocalVertexIndex(globalVertexIndex, region);
        yield return (primaryChunkId, primaryLocalIndex);

        int localX = primaryLocalIndex % ChunkVertexStride;
        int localY = primaryLocalIndex / ChunkVertexStride;
        var (chunkX, chunkY) = GetChunkCoords(primaryChunkId);

        // A vertex at (0, y) in chunk (X, Y) is also at (64, y) in chunk (X-1, Y)
        if (localX == 0 && chunkX > 0) {
            yield return (GetChunkId(chunkX - 1, chunkY), (ushort)(localY * ChunkVertexStride + (ChunkVertexStride - 1)));
        }
        
        // A vertex at (x, 0) in chunk (X, Y) is also at (x, 64) in chunk (X, Y-1)
        if (localY == 0 && chunkY > 0) {
            yield return (GetChunkId(chunkX, chunkY - 1), (ushort)((ChunkVertexStride - 1) * ChunkVertexStride + localX));
        }
        
        // A vertex at (0, 0) in chunk (X, Y) is also at (64, 64) in chunk (X-1, Y-1)
        if (localX == 0 && localY == 0 && chunkX > 0 && chunkY > 0) {
            yield return (GetChunkId(chunkX - 1, chunkY - 1), (ushort)((ChunkVertexStride - 1) * ChunkVertexStride + (ChunkVertexStride - 1)));
        }
    }

    /// <inheritdoc/>
    public ushort GetChunkIdForLandblock(uint landblockId) {
        var lbId = (ushort)(landblockId >> 16);
        uint lbX = (uint)(lbId >> 8);
        uint lbY = (uint)(lbId & 0xFF);
        return GetChunkId(lbX / (uint)LandblocksPerChunk, lbY / (uint)LandblocksPerChunk);
    }

    /// <inheritdoc/>
    public (uint x, uint y) GetChunkCoords(ushort chunkId) {
        return ((uint)(chunkId >> 8), (uint)(chunkId & 0xFF));
    }

    /// <inheritdoc/>
    public ushort GetChunkId(uint x, uint y) {
        return (ushort)((x << 8) | y);
    }

    /// <inheritdoc/>
    public IEnumerable<(int x, int y)> GetAffectedLandblocks(IEnumerable<uint> vertexIndices, ITerrainInfo region) {
        if (region == null) {
            yield break;
        }

        var affectedBlocks = new HashSet<(int x, int y)>();
        var stride = region.LandblockVerticeLength - 1;

        foreach (var vertexIndex in vertexIndices) {
            int globalY = (int)(vertexIndex / (uint)region.MapWidthInVertices);
            int globalX = (int)(vertexIndex % (uint)region.MapWidthInVertices);

            int lbX = globalX / stride;
            int lbY = globalY / stride;

            bool isXBoundary = globalX > 0 && globalX % stride == 0;
            bool isYBoundary = globalY > 0 && globalY % stride == 0;

            if (lbX < region.MapWidthInLandblocks && lbY < region.MapHeightInLandblocks) {
                affectedBlocks.Add((lbX, lbY));
            }

            if (isXBoundary && lbX > 0 && lbY < region.MapHeightInLandblocks) {
                affectedBlocks.Add((lbX - 1, lbY));
            }
            if (isYBoundary && lbY > 0 && lbX < region.MapWidthInLandblocks) {
                affectedBlocks.Add((lbX, lbY - 1));
            }
            if (isXBoundary && isYBoundary && lbX > 0 && lbY > 0) {
                affectedBlocks.Add((lbX - 1, lbY - 1));
            }
        }

        foreach (var block in affectedBlocks) {
            yield return block;
        }
    }
}
