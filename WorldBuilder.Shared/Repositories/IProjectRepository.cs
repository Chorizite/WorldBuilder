using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Repositories {
    /// <summary>
    /// Defines the contract for a project repository, handling document and event persistence.
    /// </summary>
    public interface IProjectRepository : IDisposable {
        /// <summary>
        /// Gets the path to the project directory.
        /// </summary>
        string ProjectDirectory { get; }

        /// <summary>Initializes the database schema.</summary>
        /// <param name="ct">The cancellation token.</param>
        Task InitializeDatabaseAsync(CancellationToken ct);

        /// <summary>Creates a new database transaction.</summary>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task containing the transaction.</returns>
        Task<ITransaction> CreateTransactionAsync(CancellationToken ct);

        /// <summary>Retrieves all landscape layers for a region.</summary>
        /// <param name="regionId">The region ID.</param>
        /// <param name="tx">The transaction (optional).</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task containing a list of landscape layers.</returns>
        Task<IReadOnlyList<LandscapeLayerBase>> GetLayersAsync(uint regionId, ITransaction? tx, CancellationToken ct);

        /// <summary>Upserts a landscape layer.</summary>
        /// <param name="layer">The layer to upsert.</param>
        /// <param name="regionId">The region ID it belongs to.</param>
        /// <param name="sortOrder">The sort order within its parent.</param>
        /// <param name="tx">The transaction (optional).</param>
        /// <param name="ct">The cancellation token.</param>
        Task<Result<Unit>> UpsertLayerAsync(LandscapeLayerBase layer, uint regionId, int sortOrder, ITransaction? tx, CancellationToken ct);

        /// <summary>Deletes a landscape layer or group.</summary>
        /// <param name="id">The layer or group ID.</param>
        /// <param name="tx">The transaction (optional).</param>
        /// <param name="ct">The cancellation token.</param>
        Task<Result<Unit>> DeleteLayerAsync(string id, ITransaction? tx, CancellationToken ct);

        Task<IReadOnlyList<StaticObject>> GetStaticObjectsAsync(uint? landblockId, uint? cellId, ITransaction? tx, CancellationToken ct);

        /// <summary>Retrieves all static objects for a set of landblocks.</summary>
        /// <param name="landblockIds">The landblock IDs.</param>
        /// <param name="tx">The transaction (optional).</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A dictionary mapping landblock ID to its list of static objects.</returns>
        Task<IReadOnlyDictionary<uint, IReadOnlyList<StaticObject>>> GetStaticObjectsForLandblocksAsync(IEnumerable<uint> landblockIds, ITransaction? tx, CancellationToken ct);

        /// <summary>
        /// Gets all landblock IDs that have modifications (static objects, buildings, or env cells) for a specific layer.
        /// </summary>
        /// <param name="regionId">The region ID.</param>
        /// <param name="layerId">The layer ID.</param>
        /// <param name="tx">The transaction (optional).</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A list of unique landblock IDs.</returns>
        Task<IReadOnlyList<uint>> GetAffectedLandblocksByLayerAsync(uint regionId, string layerId, ITransaction? tx, CancellationToken ct);

        /// <summary>Retrieves all buildings for a landblock.</summary>
        /// <param name="landblockId">The landblock ID.</param>
        /// <param name="tx">The transaction (optional).</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task containing a list of building objects.</returns>
        Task<IReadOnlyList<BuildingObject>> GetBuildingsAsync(uint? landblockId, ITransaction? tx, CancellationToken ct);

        /// <summary>Retrieves all buildings for a set of landblocks.</summary>
        /// <param name="landblockIds">The landblock IDs.</param>
        /// <param name="tx">The transaction (optional).</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A dictionary mapping landblock ID to its list of building objects.</returns>
        Task<IReadOnlyDictionary<uint, IReadOnlyList<BuildingObject>>> GetBuildingsForLandblocksAsync(IEnumerable<uint> landblockIds, ITransaction? tx, CancellationToken ct);

        Task<Result<Unit>> UpsertStaticObjectAsync(StaticObject obj, uint regionId, uint? landblockId, uint? cellId, ITransaction? tx, CancellationToken ct);

        /// <summary>Upserts a building object.</summary>
        /// <param name="obj">The building object to upsert.</param>
        /// <param name="regionId">The region ID it belongs to.</param>
        /// <param name="landblockId">The landblock ID it belongs to.</param>
        /// <param name="tx">The transaction (optional).</param>
        /// <param name="ct">The cancellation token.</param>
        Task<Result<Unit>> UpsertBuildingAsync(BuildingObject obj, uint regionId, uint? landblockId, ITransaction? tx, CancellationToken ct);

        /// <summary>Deletes a building by instance ID.</summary>
        /// <param name="instanceId">The instance ID.</param>
        /// <param name="tx">The transaction (optional).</param>
        /// <param name="ct">The cancellation token.</param>
        Task<Result<Unit>> DeleteBuildingAsync(ulong instanceId, ITransaction? tx, CancellationToken ct);

        /// <summary>Deletes a static object by instance ID.</summary>
        /// <param name="instanceId">The instance ID.</param>
        /// <param name="tx">The transaction (optional).</param>
        /// <param name="ct">The cancellation token.</param>
        Task<Result<Unit>> DeleteStaticObjectAsync(ulong instanceId, ITransaction? tx, CancellationToken ct);

        /// <summary>Retrieves an EnvCell by ID.</summary>
        /// <param name="cellId">The cell ID.</param>
        /// <param name="tx">The transaction (optional).</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task containing the cell result.</returns>
        Task<Result<Cell>> GetEnvCellAsync(uint cellId, ITransaction? tx, CancellationToken ct);

        /// <summary>Upserts an EnvCell.</summary>
        /// <param name="cellId">The cell ID.</param>
        /// <param name="regionId">The region ID.</param>
        /// <param name="cell">The cell data.</param>
        /// <param name="tx">The transaction (optional).</param>
        /// <param name="ct">The cancellation token.</param>
        Task<Result<Unit>> UpsertEnvCellAsync(uint cellId, uint regionId, Cell cell, ITransaction? tx, CancellationToken ct);

        /// <summary>Retrieves all terrain patch IDs for a specific region.</summary>
        /// <param name="regionId">The region ID.</param>
        /// <param name="tx">The transaction (optional).</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task containing a list of terrain patch IDs.</returns>
        Task<IReadOnlyList<string>> GetTerrainPatchIdsAsync(uint regionId, ITransaction? tx, CancellationToken ct);

        /// <summary>Retrieves a terrain patch's serialized data by its ID.</summary>
        /// <param name="id">The terrain patch ID.</param>
        /// <param name="tx">The transaction (optional).</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task containing the result with the terrain patch's byte array.</returns>
        Task<Result<byte[]>> GetTerrainPatchBlobAsync(string id, ITransaction? tx, CancellationToken ct);

        /// <summary>Inserts a new command event into the repository.</summary>
        /// <param name="evt">The command event.</param>
        /// <param name="tx">The transaction (optional).</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task representing the result of the operation.</returns>
        Task<Result<Unit>> InsertEventAsync(BaseCommand evt, ITransaction? tx, CancellationToken ct);

        /// <summary>Retrieves all terrain patches for a specific region.</summary>
        /// <param name="regionId">The region ID.</param>
        /// <param name="tx">The transaction (optional).</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task containing a list of terrain patches.</returns>
        Task<IReadOnlyList<TerrainPatch>> GetTerrainPatchesAsync(uint regionId, ITransaction? tx, CancellationToken ct);

        /// <summary>Upserts a terrain patch into the repository.</summary>
        /// <param name="id">The terrain patch ID.</param>
        /// <param name="regionId">The region ID.</param>
        /// <param name="data">The serialized terrain patch data.</param>
        /// <param name="version">The terrain patch version.</param>
        /// <param name="tx">The transaction (optional).</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task representing the result of the operation.</returns>
        Task<Result<Unit>> UpsertTerrainPatchAsync(string id, uint regionId, byte[] data, ulong version, ITransaction? tx, CancellationToken ct);

        /// <summary>Retrieves a user-specific value by key.</summary>
        /// <param name="key">The key.</param>
        /// <param name="tx">The transaction (optional).</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task containing the result with the value string.</returns>
        Task<Result<string>> GetUserValueAsync(string key, ITransaction? tx, CancellationToken ct);

        /// <summary>Updates or inserts a user-specific value.</summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value string.</param>
        /// <param name="tx">The transaction (optional).</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task representing the result of the operation.</returns>
        Task<Result<Unit>> UpsertUserValueAsync(string key, string value, ITransaction? tx, CancellationToken ct);

        /// <summary>Retrieves all events that haven't been synced with the server.</summary>
        /// <param name="tx">The transaction (optional).</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task containing a list of unsynced events.</returns>
        Task<IReadOnlyList<BaseCommand>> GetUnsyncedEventsAsync(ITransaction? tx, CancellationToken ct);

        /// <summary>Updates the server timestamp for a specific event.</summary>
        /// <param name="eventId">The event ID.</param>
        /// <param name="serverTimestamp">The server timestamp.</param>
        /// <param name="tx">The transaction (optional).</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task representing the result of the operation.</returns>
        Task<Result<Unit>> UpdateEventServerTimestampAsync(string eventId, ulong serverTimestamp, ITransaction? tx,
            CancellationToken ct);
    }
}