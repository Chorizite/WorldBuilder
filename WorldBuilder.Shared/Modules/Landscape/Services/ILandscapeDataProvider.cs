using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Modules.Landscape.Services
{
    /// <summary>
    /// Provides merged landscape data from multiple sources (DAT, SQL, etc.).
    /// </summary>
    public interface ILandscapeDataProvider
    {
        /// <summary>
        /// Gets the merged landblock data, including static objects and buildings.
        /// </summary>
        /// <param name="landblockId">The landblock ID.</param>
        /// <param name="cellDatabase">The cell database to read from.</param>
        /// <param name="visibleLayerIds">The set of visible layer IDs.</param>
        /// <param name="baseLayerId">The ID of the base layer.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The merged landblock data.</returns>
        Task<MergedLandblock> GetMergedLandblockAsync(uint landblockId, IDatDatabase? cellDatabase, IEnumerable<string> visibleLayerIds, string? baseLayerId, CancellationToken ct);

        /// <summary>
        /// Gets merged landblock data for multiple landblocks in a single operation.
        /// </summary>
        /// <param name="landblockIds">The landblock IDs.</param>
        /// <param name="cellDatabase">The cell database to read from.</param>
        /// <param name="visibleLayerIds">The set of visible layer IDs.</param>
        /// <param name="baseLayerId">The ID of the base layer.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A dictionary mapping landblock ID to its merged data.</returns>
        Task<IReadOnlyDictionary<uint, MergedLandblock>> GetMergedLandblocksAsync(IEnumerable<uint> landblockIds, IDatDatabase? cellDatabase, IEnumerable<string> visibleLayerIds, string? baseLayerId, CancellationToken ct);

        /// <summary>
        /// Gets the merged environment cell data, including properties and static objects.
        /// </summary>
        /// <param name="cellId">The cell ID.</param>
        /// <param name="cellDatabase">The cell database to read from.</param>
        /// <param name="visibleLayerIds">The set of visible layer IDs.</param>
        /// <param name="baseLayerId">The ID of the base layer.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The merged environment cell data.</returns>
        Task<Cell> GetMergedEnvCellAsync(uint cellId, IDatDatabase? cellDatabase, IEnumerable<string> visibleLayerIds, string? baseLayerId, CancellationToken ct);

        /// <summary>
        /// Gets merged terrain data for a region, applying changes from visible layers.
        /// </summary>
        /// <param name="regionId">The region ID.</param>
        /// <param name="visibleLayerIds">The set of visible layer IDs.</param>
        /// <param name="regionInfo">The region info for vertex index calculation.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A dictionary mapping global vertex index to its merged terrain entry.</returns>
        Task<IReadOnlyDictionary<uint, TerrainEntry>> GetMergedTerrainAsync(uint regionId, IEnumerable<string> visibleLayerIds, ITerrainInfo regionInfo, CancellationToken ct);
    }
}
