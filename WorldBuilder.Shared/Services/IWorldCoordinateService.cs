using System.Collections.Generic;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Services;

/// <summary>
/// Service for handling coordinate translations and boundary logic.
/// </summary>
public interface IWorldCoordinateService {
    /// <summary>
    /// Gets the primary chunk ID and local index for a global vertex index.
    /// The primary chunk is the one where the vertex is in the range [0, 63].
    /// </summary>
    (ushort chunkId, ushort localIndex) GetLocalVertexIndex(uint globalVertexIndex, ITerrainInfo region);

    /// <summary>
    /// Gets the global vertex index for a given chunk ID and local index.
    /// </summary>
    uint GetGlobalVertexIndex(ushort chunkId, ushort localIndex, ITerrainInfo region);

    /// <summary>
    /// Gets all chunks and their local indices that are affected by a global vertex index.
    /// This includes the primary chunk and any "ghost" vertices in adjacent chunks.
    /// </summary>
    IEnumerable<(ushort chunkId, ushort localIndex)> GetAffectedChunksWithBoundaries(uint globalVertexIndex, ITerrainInfo region);

    /// <summary>
    /// Gets the chunk ID for a given landblock ID.
    /// </summary>
    ushort GetChunkIdForLandblock(uint landblockId);
    
    /// <summary>
    /// Gets the chunk coordinates (x, y) for a given chunk ID.
    /// </summary>
    (uint x, uint y) GetChunkCoords(ushort chunkId);

    /// <summary>
    /// Gets the chunk ID for a given chunk coordinate (x, y).
    /// </summary>
    ushort GetChunkId(uint x, uint y);

    /// <summary>
    /// Gets the landblock coordinates (x, y) affected by a set of vertex indices.
    /// Handles vertices that are on boundaries between landblocks.
    /// </summary>
    IEnumerable<(int x, int y)> GetAffectedLandblocks(IEnumerable<uint> vertexIndices, ITerrainInfo region);
}
