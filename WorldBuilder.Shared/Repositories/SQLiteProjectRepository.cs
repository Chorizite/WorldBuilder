using FluentMigrator.Runner;
using MemoryPack;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Data.Common;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Migrations;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace WorldBuilder.Shared.Repositories {
    /// <summary>
    /// A SQLite-based implementation of <see cref="IProjectRepository"/>.
    /// </summary>
    public class SQLiteProjectRepository : IProjectRepository {
        /// <summary>The underlying SQLite connection.</summary>
        public readonly SqliteConnection Connection;
        private readonly ILogger<SQLiteProjectRepository>? _logger;

        /// <inheritdoc/>
        public string ProjectDirectory { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SQLiteProjectRepository"/> class.
        /// </summary>
        /// <param name="connectionString">The SQLite connection string.</param>
        /// <param name="logger">The logger (optional).</param>
        public SQLiteProjectRepository(string connectionString, ILogger<SQLiteProjectRepository>? logger = null) {
            Connection = new SqliteConnection(connectionString);
            Connection.Open();

            // Extract project directory from connection string
            var dataSource = Connection.DataSource;
            if (dataSource.StartsWith("file:")) {
                var path = dataSource.Substring(5).Split('?')[0];
                ProjectDirectory = Path.GetDirectoryName(path) ?? string.Empty;
            }
            else if (dataSource == ":memory:") {
                ProjectDirectory = string.Empty;
            }
            else {
                ProjectDirectory = Path.GetDirectoryName(dataSource) ?? string.Empty;
            }

            // Enable WAL mode for better performance and concurrency
            using (var cmd = Connection.CreateCommand()) {
                cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
                cmd.ExecuteNonQuery();
            }

            _logger = logger ?? NullLogger<SQLiteProjectRepository>.Instance;
        }

        /// <inheritdoc/>
        public Task InitializeDatabaseAsync(CancellationToken ct) {
            _logger?.LogInformation("Initializing database");
            SQLitePCL.Batteries_V2.Init();

            var serviceProvider = CreateServices();
            using var scope = serviceProvider.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
            runner.MigrateUp();
            _logger?.LogInformation("Database initialized successfully");
            return Task.CompletedTask;
        }

        private IServiceProvider CreateServices() {
            return new ServiceCollection()
                .AddFluentMigratorCore()
                .ConfigureRunner(rb => rb
                    .AddSQLite()
                    .WithGlobalConnectionString(Connection.ConnectionString)
                    .ScanIn(typeof(Migration_001_InitialSchema).Assembly).For.Migrations())
                .BuildServiceProvider(false);
        }

        /// <inheritdoc/>
        public async Task<ITransaction> CreateTransactionAsync(CancellationToken ct) {
            var dbTransaction = await Connection.BeginTransactionAsync(ct);
            return new DatabaseTransactionAdapter(dbTransaction);
        }

        private static Result<SqliteTransaction?> GetDbTransaction(ITransaction? transaction) {
            transaction ??= TransactionContext.Current;

            if (transaction == null) {
                return Result<SqliteTransaction?>.Success(null);
            }

            if (transaction is DatabaseTransactionAdapter adapter) {
                var sqliteTransaction = adapter.UnderlyingTransaction as SqliteTransaction;
                if (sqliteTransaction == null) {
                    return Result<SqliteTransaction?>.Failure(
                        $"Transaction does not contain a valid SqliteTransaction. Type: {adapter.UnderlyingTransaction?.GetType()}",
                        "TRANSACTION_ERROR");
                }

                return Result<SqliteTransaction?>.Success(sqliteTransaction);
            }

            return Result<SqliteTransaction?>.Failure($"Transaction type {transaction.GetType().Name} is not supported",
                "TRANSACTION_ERROR");
        }

        /// <inheritdoc/>
        public async Task<Result<string>> GetUserValueAsync(string key, ITransaction? tx, CancellationToken ct) {
            try {
                _logger?.LogDebug("Retrieving user value for key: {Key}", key);
                var dbTxResult = GetDbTransaction(tx);
                if (dbTxResult.IsFailure) return Result<string>.Failure(dbTxResult.Error);
                var dbTx = dbTxResult.Value;

                var sql = "SELECT Value FROM UserKeyValues WHERE Key = @key";
                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@key", key);
                var obj = await cmd.ExecuteScalarAsync(ct);
                if (obj == null) {
                    _logger?.LogDebug("User value for key {Key} not found", key);
                    return Result<string>.Failure($"User value for key {key} not found", "USER_VALUE_NOT_FOUND");
                }
                else {
                    _logger?.LogDebug("User value for key {Key} retrieved successfully", key);
                    return Result<string>.Success((string)obj!);
                }
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error retrieving user value for key: {Key}", key);
                return Result<string>.Failure($"Error retrieving user value: {ex.Message}", "DATABASE_ERROR");
            }
        }

        /// <inheritdoc/>
        public async Task<Result<Unit>> UpsertUserValueAsync(string key, string value, ITransaction? tx,
            CancellationToken ct) {
            try {
                _logger?.LogDebug("Upserting user value for key: {Key}", key);
                var dbTxResult = GetDbTransaction(tx);
                if (dbTxResult.IsFailure) {
                    return Result<Unit>.Failure(dbTxResult.Error);
                }

                var dbTx = dbTxResult.Value;
                const string sql = @"
        INSERT INTO UserKeyValues (Key, Value)
        VALUES (@key, @value)
        ON CONFLICT(Key) DO UPDATE SET Value = @value";

                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@key", key);
                cmd.Parameters.AddWithValue("@value", value);
                await cmd.ExecuteNonQueryAsync(ct);
                _logger?.LogDebug("User value for key {Key} upserted successfully", key);
                return Result<Unit>.Success(Unit.Value);
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error upserting user value for key: {Key}", key);
                return Result<Unit>.Failure($"Error upserting user value: {ex.Message}", "DATABASE_ERROR");
            }
        }

        /// <inheritdoc/>
        public async Task<Result<Unit>> InsertEventAsync(BaseCommand evt, ITransaction? tx, CancellationToken ct) {
            try {
                _logger?.LogDebug("Inserting event {EventId} of type {EventType} for user {UserId}", evt.Id,
                    evt.GetType().Name, evt.UserId);
                var dbTxResult = GetDbTransaction(tx);
                if (dbTxResult.IsFailure) {
                    return Result<Unit>.Failure(dbTxResult.Error);
                }

                var dbTx = dbTxResult.Value;
                if (string.IsNullOrEmpty(evt.Id)) {
                    return Result<Unit>.Failure("Event Id cannot be null or empty", "ARGUMENT_ERROR");
                }

                if (string.IsNullOrEmpty(evt.UserId)) {
                    return Result<Unit>.Failure("UserId cannot be null or empty", "ARGUMENT_ERROR");
                }

                const string sql = @"
                    INSERT INTO Events (Id, Type, Data, UserId)
                    VALUES (@id, @type, @data, @uid)";

                var data = evt.Serialize();
                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", evt.Id);
                cmd.Parameters.AddWithValue("@type", evt.GetType().Name);
                cmd.Parameters.AddWithValue("@data", data);
                cmd.Parameters.AddWithValue("@uid", evt.UserId);
                await cmd.ExecuteNonQueryAsync(ct);
                _logger?.LogDebug("Event {EventId} inserted successfully", evt.Id);
                return Result<Unit>.Success(Unit.Value);
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error inserting event {EventId} of type {EventType}", evt.Id,
                    evt.GetType().Name);
                return Result<Unit>.Failure($"Error inserting event: {ex.Message}", "DATABASE_ERROR");
            }
        }

        /// <inheritdoc/>
        public async Task<Result<Unit>> UpsertTerrainPatchAsync(string id, uint regionId, byte[] data, ulong version,
            ITransaction? tx, CancellationToken ct) {
            try {
                _logger?.LogDebug("Upserting terrain patch with ID: {DocumentId}, Region: {RegionId}, Version: {Version}",
                    id, regionId, version);
                var dbTxResult = GetDbTransaction(tx);
                if (dbTxResult.IsFailure) {
                    return Result<Unit>.Failure(dbTxResult.Error);
                }

                var dbTx = dbTxResult.Value;

                const string sql = @"
                    INSERT INTO TerrainPatches (Id, RegionId, Data, Version, LastModified)
                    VALUES (@id, @regionId, @data, @ver, CURRENT_TIMESTAMP)
                    ON CONFLICT(Id) DO UPDATE SET
                        Data = @data,
                        Version = @ver,
                        LastModified = CURRENT_TIMESTAMP";

                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@regionId", (long)regionId);
                cmd.Parameters.AddWithValue("@data", data);
                cmd.Parameters.AddWithValue("@ver", (long)version);

                await cmd.ExecuteNonQueryAsync(ct);
                _logger?.LogDebug("Terrain patch with ID {DocumentId} upserted successfully", id);
                return Result<Unit>.Success(Unit.Value);
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error upserting terrain patch with ID: {DocumentId}", id);
                return Result<Unit>.Failure($"Error upserting terrain patch: {ex.Message}", "DATABASE_ERROR");
            }
        }


        /// <inheritdoc/>
        public async Task<IReadOnlyList<string>> GetTerrainPatchIdsAsync(uint regionId, ITransaction? tx, CancellationToken ct) {
            var ids = new List<string>();
            try {
                _logger?.LogDebug("Retrieving terrain patch IDs for region: {RegionId}", regionId);
                var dbTxResult = GetDbTransaction(tx);
                var dbTx = dbTxResult.IsSuccess ? dbTxResult.Value : null;

                const string sql = "SELECT Id FROM TerrainPatches WHERE RegionId = @regionId";
                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@regionId", (long)regionId);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) {
                    ids.Add(reader.GetString(0));
                }
                _logger?.LogDebug("Retrieved {Count} terrain patch IDs for region: {RegionId}", ids.Count, regionId);
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error retrieving terrain patch IDs for region: {RegionId}", regionId);
            }
            return ids;
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<TerrainPatch>> GetTerrainPatchesAsync(uint regionId, ITransaction? tx, CancellationToken ct) {
            var patches = new List<TerrainPatch>();
            try {
                _logger?.LogDebug("Retrieving all terrain patches for region: {RegionId}", regionId);
                var dbTxResult = GetDbTransaction(tx);
                var dbTx = dbTxResult.IsSuccess ? dbTxResult.Value : null;

                const string sql = "SELECT Id, RegionId, Data, Version, LastModified FROM TerrainPatches WHERE RegionId = @regionId";
                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@regionId", (long)regionId);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) {
                    patches.Add(new TerrainPatch {
                        Id = reader.GetString(0),
                        RegionId = (uint)reader.GetInt64(1),
                        Data = (byte[])reader["Data"],
                        Version = (ulong)reader.GetInt64(3),
                        LastModified = reader.GetDateTime(4)
                    });
                }
                _logger?.LogDebug("Retrieved {Count} terrain patches for region: {RegionId}", patches.Count, regionId);
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error retrieving terrain patches for region: {RegionId}", regionId);
            }
            return patches;
        }

        /// <inheritdoc/>
        public async Task<Result<byte[]>> GetTerrainPatchBlobAsync(string id, ITransaction? tx, CancellationToken ct) {
            try {
                _logger?.LogDebug("Retrieving terrain patch blob with ID: {DocumentId}", id);
                var dbTxResult = GetDbTransaction(tx);
                if (dbTxResult.IsFailure) return Result<byte[]>.Failure(dbTxResult.Error);
                var dbTx = dbTxResult.Value;

                const string sql = "SELECT Data FROM TerrainPatches WHERE Id = @id";
                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", id);
                var obj = await cmd.ExecuteScalarAsync(ct);
                if (obj == null) {
                    _logger?.LogWarning("Terrain patch with ID {DocumentId} not found in database", id);
                    return Result<byte[]>.Failure($"Terrain patch with ID {id} not found in database", "DOCUMENT_NOT_FOUND");
                }
                else {
                    _logger?.LogDebug("Terrain patch blob with ID {DocumentId} retrieved successfully", id);
                    return Result<byte[]>.Success((byte[])obj!);
                }
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error retrieving terrain patch blob with ID: {DocumentId}", id);
                return Result<byte[]>.Failure($"Error retrieving terrain patch: {ex.Message}", "DATABASE_ERROR");
            }
        }


        /// <inheritdoc/>
        public async Task<IReadOnlyList<BaseCommand>> GetUnsyncedEventsAsync(ITransaction? tx, CancellationToken ct) {
            var events = new List<BaseCommand>();
            try {
                _logger?.LogDebug("Retrieving unsynced events");
                var dbTxResult = GetDbTransaction(tx);
                var dbTx = dbTxResult.IsSuccess ? dbTxResult.Value : null;

                const string sql = "SELECT Data FROM Events WHERE ServerTimestamp IS NULL ORDER BY Created ASC";
                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) {
                    var data = (byte[])reader["Data"];
                    var evt = BaseCommand.Deserialize(data);
                    if (evt != null) {
                        events.Add(evt);
                    }
                }

                _logger?.LogDebug("Retrieved {Count} unsynced events", events.Count);
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error retrieving unsynced events");
            }

            return events;
        }

        /// <inheritdoc/>
        public async Task<Result<Unit>> UpdateEventServerTimestampAsync(string eventId, ulong serverTimestamp,
            ITransaction? tx, CancellationToken ct) {
            try {
                _logger?.LogDebug("Updating ServerTimestamp for event {EventId} to {Timestamp}", eventId,
                    serverTimestamp);
                var dbTxResult = GetDbTransaction(tx);
                if (dbTxResult.IsFailure) {
                    return Result<Unit>.Failure(dbTxResult.Error);
                }

                var dbTx = dbTxResult.Value;
                const string sql = "UPDATE Events SET ServerTimestamp = @ts WHERE Id = @id";

                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", eventId);
                cmd.Parameters.AddWithValue("@ts", (long)serverTimestamp);
                await cmd.ExecuteNonQueryAsync(ct);
                _logger?.LogDebug("ServerTimestamp updated for event {EventId}", eventId);
                return Result<Unit>.Success(Unit.Value);
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error updating ServerTimestamp for event {EventId}", eventId);
                return Result<Unit>.Failure($"Error updating event timestamp: {ex.Message}", "DATABASE_ERROR");
            }
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<LandscapeLayerBase>> GetLayersAsync(uint regionId, ITransaction? tx, CancellationToken ct) {
            var items = new List<LandscapeLayerBase>();
            try {
                var dbTxResult = GetDbTransaction(tx);
                var dbTx = dbTxResult.IsSuccess ? dbTxResult.Value : null;

                // Load Groups
                const string groupSql = "SELECT Id, Name, ParentId, IsExported, SortOrder FROM LandscapeGroups WHERE RegionId = @regionId";
                await using (var cmd = Connection.CreateCommand()) {
                    cmd.Transaction = dbTx;
                    cmd.CommandText = groupSql;
                    cmd.Parameters.AddWithValue("@regionId", (long)regionId);
                    await using var reader = await cmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct)) {
                        items.Add(new LandscapeLayerGroup {
                            Id = reader.GetString(0),
                            Name = reader.GetString(1),
                            ParentId = reader.IsDBNull(2) ? null : reader.GetString(2),
                            IsExported = reader.GetBoolean(3)
                        });
                    }
                }

                // Load Layers
                const string layerSql = "SELECT Id, Name, ParentId, IsExported, IsBase, SortOrder FROM LandscapeLayers WHERE RegionId = @regionId";
                await using (var cmd = Connection.CreateCommand()) {
                    cmd.Transaction = dbTx;
                    cmd.CommandText = layerSql;
                    cmd.Parameters.AddWithValue("@regionId", (long)regionId);
                    await using var reader = await cmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct)) {
                        items.Add(new LandscapeLayer {
                            Id = reader.GetString(0),
                            Name = reader.GetString(1),
                            ParentId = reader.IsDBNull(2) ? null : reader.GetString(2),
                            IsExported = reader.GetBoolean(3),
                            IsBase = reader.GetBoolean(4)
                        });
                    }
                }
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error retrieving layers for region {RegionId}", regionId);
            }
            return items;
        }

        /// <inheritdoc/>
        public async Task<Result<Unit>> UpsertLayerAsync(LandscapeLayerBase layer, uint regionId, int sortOrder, ITransaction? tx, CancellationToken ct) {
            try {
                var dbTxResult = GetDbTransaction(tx);
                if (dbTxResult.IsFailure) return Result<Unit>.Failure(dbTxResult.Error);
                var dbTx = dbTxResult.Value;

                string sql;
                if (layer is LandscapeLayerGroup) {
                    sql = @"
                        INSERT INTO LandscapeGroups (Id, RegionId, Name, ParentId, IsExported, SortOrder)
                        VALUES (@id, @regionId, @name, @parentId, @isExported, @sortOrder)
                        ON CONFLICT(Id) DO UPDATE SET
                            Name = @name,
                            ParentId = @parentId,
                            IsExported = @isExported,
                            SortOrder = @sortOrder";
                }
                else {
                    sql = @"
                        INSERT INTO LandscapeLayers (Id, RegionId, Name, ParentId, IsExported, IsBase, SortOrder)
                        VALUES (@id, @regionId, @name, @parentId, @isExported, @isBase, @sortOrder)
                        ON CONFLICT(Id) DO UPDATE SET
                            Name = @name,
                            ParentId = @parentId,
                            IsExported = @isExported,
                            IsBase = @isBase,
                            SortOrder = @sortOrder";
                }

                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", layer.Id);
                cmd.Parameters.AddWithValue("@regionId", (long)regionId);
                cmd.Parameters.AddWithValue("@name", layer.Name);
                cmd.Parameters.AddWithValue("@parentId", (object?)layer.ParentId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@isExported", layer.IsExported);
                cmd.Parameters.AddWithValue("@sortOrder", sortOrder);

                if (layer is LandscapeLayer l) {
                    cmd.Parameters.AddWithValue("@isBase", l.IsBase);
                }

                await cmd.ExecuteNonQueryAsync(ct);
                return Result<Unit>.Success(Unit.Value);
            }
            catch (Exception ex) {
                return Result<Unit>.Failure($"Error upserting layer: {ex.Message}", "DATABASE_ERROR");
            }
        }

        public async Task<Result<Unit>> DeleteLayerAsync(string id, ITransaction? tx, CancellationToken ct) {
            try {
                var dbTxResult = GetDbTransaction(tx);
                if (dbTxResult.IsFailure) return Result<Unit>.Failure(dbTxResult.Error);
                var dbTx = dbTxResult.Value;

                // Try deleting from both tables
                const string groupSql = "DELETE FROM LandscapeGroups WHERE Id = @id";
                const string layerSql = "DELETE FROM LandscapeLayers WHERE Id = @id";

                await using (var cmd = Connection.CreateCommand()) {
                    cmd.Transaction = dbTx;
                    cmd.CommandText = groupSql;
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                await using (var cmd = Connection.CreateCommand()) {
                    cmd.Transaction = dbTx;
                    cmd.CommandText = layerSql;
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                return Result<Unit>.Success(Unit.Value);
            }
            catch (Exception ex) {
                return Result<Unit>.Failure($"Error deleting layer: {ex.Message}", "DATABASE_ERROR");
            }
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<uint>> GetAffectedLandblocksByLayerAsync(uint regionId, string layerId, ITransaction? tx, CancellationToken ct) {
            var landblockIds = new HashSet<uint>();
            try {
                var dbTxResult = GetDbTransaction(tx);
                var dbTx = dbTxResult.IsSuccess ? dbTxResult.Value : null;

                // 1. Static Objects
                const string staticSql = "SELECT DISTINCT LandblockId FROM StaticObjects WHERE RegionId = @regionId AND LayerId = @layerId AND LandblockId IS NOT NULL";
                await using (var cmd = Connection.CreateCommand()) {
                    cmd.Transaction = dbTx;
                    cmd.CommandText = staticSql;
                    cmd.Parameters.AddWithValue("@regionId", (long)regionId);
                    cmd.Parameters.AddWithValue("@layerId", layerId);
                    await using var reader = await cmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct)) {
                        var lbId = (uint)reader.GetInt64(0);
                        landblockIds.Add(lbId >> 16);
                    }
                }

                // 2. Buildings
                const string buildingSql = "SELECT DISTINCT LandblockId FROM Buildings WHERE RegionId = @regionId AND LayerId = @layerId AND LandblockId IS NOT NULL";
                await using (var cmd = Connection.CreateCommand()) {
                    cmd.Transaction = dbTx;
                    cmd.CommandText = buildingSql;
                    cmd.Parameters.AddWithValue("@regionId", (long)regionId);
                    cmd.Parameters.AddWithValue("@layerId", layerId);
                    await using var reader = await cmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct)) {
                        var lbId = (uint)reader.GetInt64(0);
                        landblockIds.Add(lbId >> 16);
                    }
                }

                // 3. EnvCells
                const string envCellSql = "SELECT DISTINCT CellId FROM EnvCells WHERE RegionId = @regionId AND LayerId = @layerId";
                await using (var cmd = Connection.CreateCommand()) {
                    cmd.Transaction = dbTx;
                    cmd.CommandText = envCellSql;
                    cmd.Parameters.AddWithValue("@regionId", (long)regionId);
                    cmd.Parameters.AddWithValue("@layerId", layerId);
                    await using var reader = await cmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct)) {
                        var cellId = (uint)reader.GetInt64(0);
                        landblockIds.Add(cellId >> 16);
                    }
                }
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error retrieving affected landblocks for layer {LayerId} in region {RegionId}", layerId, regionId);
            }
            return landblockIds.ToList();
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<StaticObject>> GetStaticObjectsAsync(uint? landblockId, uint? cellId, ITransaction? tx, CancellationToken ct) {
            var objects = new List<StaticObject>();
            try {
                var dbTxResult = GetDbTransaction(tx);
                var dbTx = dbTxResult.IsSuccess ? dbTxResult.Value : null;

                var sql = "SELECT InstanceId, ModelId, PosX, PosY, PosZ, RotW, RotX, RotY, RotZ, LayerId, IsDeleted FROM StaticObjects WHERE 1=1";
                if (landblockId != null && cellId == null) sql += " AND LandblockId = @lbId AND CellId IS NULL";
                else if (landblockId != null) sql += " AND LandblockId = @lbId";
                
                if (cellId != null) sql += " AND CellId = @cellId";
                
                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                if (landblockId != null) cmd.Parameters.AddWithValue("@lbId", (long)landblockId);
                if (cellId != null) cmd.Parameters.AddWithValue("@cellId", (long)cellId);
                
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) {
                    objects.Add(new StaticObject {
                        InstanceId = (ulong)reader.GetInt64(0),
                        SetupId = (uint)reader.GetInt64(1),
                        Position = new System.Numerics.Vector3(reader.GetFloat(2), reader.GetFloat(3), reader.GetFloat(4)),
                        Rotation = new System.Numerics.Quaternion(reader.GetFloat(6), reader.GetFloat(7), reader.GetFloat(8), reader.GetFloat(5)),
                        LayerId = reader.GetString(9),
                        IsDeleted = reader.GetInt32(10) != 0
                    });
                }
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error retrieving static objects for landblock {LandblockId}", landblockId);
            }
            return objects;
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyDictionary<uint, IReadOnlyList<StaticObject>>> GetStaticObjectsForLandblocksAsync(IEnumerable<uint> landblockIds, ITransaction? tx, CancellationToken ct) {
            var results = new Dictionary<uint, List<StaticObject>>();
            var ids = landblockIds.ToList();
            if (ids.Count == 0) return new Dictionary<uint, IReadOnlyList<StaticObject>>();

            try {
                var dbTxResult = GetDbTransaction(tx);
                var dbTx = dbTxResult.IsSuccess ? dbTxResult.Value : null;

                var sql = $"SELECT InstanceId, ModelId, PosX, PosY, PosZ, RotW, RotX, RotY, RotZ, LayerId, IsDeleted, LandblockId FROM StaticObjects WHERE CellId IS NULL AND LandblockId IN ({string.Join(",", ids.Select(i => (long)i))})";
                
                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) {
                    var lbId = (uint)reader.GetInt64(11);
                    if (!results.TryGetValue(lbId, out var list)) {
                        list = new List<StaticObject>();
                        results[lbId] = list;
                    }

                    list.Add(new StaticObject {
                        InstanceId = (ulong)reader.GetInt64(0),
                        SetupId = (uint)reader.GetInt64(1),
                        Position = new System.Numerics.Vector3(reader.GetFloat(2), reader.GetFloat(3), reader.GetFloat(4)),
                        Rotation = new System.Numerics.Quaternion(reader.GetFloat(6), reader.GetFloat(7), reader.GetFloat(8), reader.GetFloat(5)),
                        LayerId = reader.GetString(9),
                        IsDeleted = reader.GetInt32(10) != 0
                    });
                }
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error retrieving static objects for multiple landblocks");
            }
            return results.ToDictionary(k => k.Key, v => (IReadOnlyList<StaticObject>)v.Value);
        }

        /// <inheritdoc/>
        public async Task<Result<Unit>> UpsertStaticObjectAsync(StaticObject obj, uint regionId, uint? landblockId, uint? cellId, ITransaction? tx, CancellationToken ct) {
            try {
                var dbTxResult = GetDbTransaction(tx);
                if (dbTxResult.IsFailure) return Result<Unit>.Failure(dbTxResult.Error);
                var dbTx = dbTxResult.Value;

                const string sql = @"
                    INSERT INTO StaticObjects (InstanceId, RegionId, LayerId, LandblockId, CellId, ModelId, PosX, PosY, PosZ, RotW, RotX, RotY, RotZ, IsDeleted)
                    VALUES (@id, @regionId, @layerId, @lbId, @cellId, @modelId, @px, @py, @pz, @rw, @rx, @ry, @rz, @isDeleted)
                    ON CONFLICT(InstanceId) DO UPDATE SET
                        RegionId = @regionId,
                        LayerId = @layerId,
                        LandblockId = @lbId,
                        CellId = @cellId,
                        ModelId = @modelId,
                        PosX = @px, PosY = @py, PosZ = @pz,
                        RotW = @rw, RotX = @rx, RotY = @ry, RotZ = @rz,
                        IsDeleted = @isDeleted";

                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", (long)obj.InstanceId);
                cmd.Parameters.AddWithValue("@regionId", (long)regionId);
                cmd.Parameters.AddWithValue("@layerId", obj.LayerId);
                cmd.Parameters.AddWithValue("@lbId", (object?)landblockId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@cellId", (object?)cellId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@modelId", (long)obj.SetupId);
                cmd.Parameters.AddWithValue("@px", obj.Position.X);
                cmd.Parameters.AddWithValue("@py", obj.Position.Y);
                cmd.Parameters.AddWithValue("@pz", obj.Position.Z);
                cmd.Parameters.AddWithValue("@rw", obj.Rotation.W);
                cmd.Parameters.AddWithValue("@rx", obj.Rotation.X);
                cmd.Parameters.AddWithValue("@ry", obj.Rotation.Y);
                cmd.Parameters.AddWithValue("@rz", obj.Rotation.Z);
                cmd.Parameters.AddWithValue("@isDeleted", obj.IsDeleted ? 1 : 0);
                await cmd.ExecuteNonQueryAsync(ct);
                return Result<Unit>.Success(Unit.Value);
            }
            catch (Exception ex) {
                return Result<Unit>.Failure($"Error upserting static object: {ex.Message}", "DATABASE_ERROR");
            }
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<BuildingObject>> GetBuildingsAsync(uint? landblockId, ITransaction? tx, CancellationToken ct) {
            var sql = @"
                SELECT ModelId, PosX, PosY, PosZ, RotW, RotX, RotY, RotZ, InstanceId, LayerId, NumLeaves, IsDeleted
                FROM Buildings 
                WHERE 1=1";
            if (landblockId != null) sql += " AND LandblockId = @lbId";

            var results = new List<BuildingObject>();
            try {
                var dbTxResult = GetDbTransaction(tx);
                var dbTx = dbTxResult.IsSuccess ? dbTxResult.Value : null;

                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                if (landblockId != null) cmd.Parameters.AddWithValue("@lbId", (long)landblockId);
                
                var buildingMap = new Dictionary<ulong, BuildingObject>();
                await using (var reader = await cmd.ExecuteReaderAsync(ct)) {
                    while (await reader.ReadAsync(ct)) {
                        var bldg = new BuildingObject {
                            ModelId = (uint)reader.GetInt64(0),
                            Position = new System.Numerics.Vector3(reader.GetFloat(1), reader.GetFloat(2), reader.GetFloat(3)),
                            Rotation = new System.Numerics.Quaternion(reader.GetFloat(5), reader.GetFloat(6), reader.GetFloat(7), reader.GetFloat(4)),
                            InstanceId = (ulong)reader.GetInt64(8),
                            LayerId = reader.GetString(9),
                            NumLeaves = (uint)reader.GetInt64(10),
                            IsDeleted = reader.GetBoolean(11),
                            Portals = new List<WbBuildingPortal>()
                        };
                        buildingMap[bldg.InstanceId] = bldg;
                        results.Add(bldg);
                    }
                }

                if (buildingMap.Count > 0) {
                    var instanceIds = string.Join(",", buildingMap.Keys);
                    var portalSql = $"SELECT Id, InstanceId, Flags, OtherCellId, OtherPortalId FROM BuildingPortals WHERE InstanceId IN ({instanceIds})";
                    var portalsById = new Dictionary<long, WbBuildingPortal>();

                    await using (var portalCmd = Connection.CreateCommand()) {
                        portalCmd.Transaction = dbTx;
                        portalCmd.CommandText = portalSql;
                        await using var portalReader = await portalCmd.ExecuteReaderAsync(ct);
                        while (await portalReader.ReadAsync(ct)) {
                            var id = portalReader.GetInt64(0);
                            var instId = (ulong)portalReader.GetInt64(1);
                            var portal = new WbBuildingPortal {
                                Flags = (uint)portalReader.GetInt64(2),
                                OtherCellId = (ushort)portalReader.GetInt32(3),
                                OtherPortalId = (ushort)portalReader.GetInt32(4),
                                StabList = new List<ushort>()
                            };
                            portalsById[id] = portal;
                            if (buildingMap.TryGetValue(instId, out var bldg)) {
                                bldg.Portals.Add(portal);
                            }
                        }
                    }

                    if (portalsById.Count > 0) {
                        var portalIds = string.Join(",", portalsById.Keys);
                        var stabSql = $"SELECT PortalId, StabId FROM BuildingPortalStabs WHERE PortalId IN ({portalIds})";
                        await using (var stabCmd = Connection.CreateCommand()) {
                            stabCmd.Transaction = dbTx;
                            stabCmd.CommandText = stabSql;
                            await using var stabReader = await stabCmd.ExecuteReaderAsync(ct);
                            while (await stabReader.ReadAsync(ct)) {
                                var portalId = stabReader.GetInt64(0);
                                var stabId = (ushort)stabReader.GetInt32(1);
                                if (portalsById.TryGetValue(portalId, out var portal)) {
                                    portal.StabList.Add(stabId);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error getting buildings for landblock {LandblockId}", landblockId);
            }
            return results;
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyDictionary<uint, IReadOnlyList<uint>>> GetEnvCellIdsForLandblocksAsync(IEnumerable<uint> landblockIds, ITransaction? tx, CancellationToken ct) {
            var lbIds = landblockIds.ToList();
            if (lbIds.Count == 0) return new Dictionary<uint, IReadOnlyList<uint>>();

            var results = new Dictionary<uint, List<uint>>();
            try {
                var dbTxResult = GetDbTransaction(tx);
                var dbTx = dbTxResult.IsSuccess ? dbTxResult.Value : null;

                var idString = string.Join(",", lbIds.Select(i => (long)i));
                var sql = $"SELECT DISTINCT CellId, (CellId & 0xFFFF0000) as LandblockId FROM EnvCells WHERE (CellId & 0xFFFF0000) IN ({idString})";

                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) {
                    var cellId = (uint)reader.GetInt64(0);
                    var lbId = (uint)reader.GetInt64(1);

                    if (!results.TryGetValue(lbId, out var list)) {
                        list = new List<uint>();
                        results[lbId] = list;
                    }
                    list.Add(cellId);
                }
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error getting env cell IDs for landblocks");
            }
            return results.ToDictionary(k => k.Key, k => (IReadOnlyList<uint>)k.Value);
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyDictionary<uint, IReadOnlyList<BuildingObject>>> GetBuildingsForLandblocksAsync(IEnumerable<uint> landblockIds, ITransaction? tx, CancellationToken ct) {
            var lbIds = landblockIds.ToList();
            if (lbIds.Count == 0) return new Dictionary<uint, IReadOnlyList<BuildingObject>>();

            var results = new Dictionary<uint, List<BuildingObject>>();
            try {
                var dbTxResult = GetDbTransaction(tx);
                var dbTx = dbTxResult.IsSuccess ? dbTxResult.Value : null;

                var idString = string.Join(",", lbIds.Select(i => (long)i));
                var sql = $@"
                    SELECT ModelId, PosX, PosY, PosZ, RotW, RotX, RotY, RotZ, InstanceId, LayerId, NumLeaves, IsDeleted, LandblockId
                    FROM Buildings 
                    WHERE LandblockId IN ({idString})";

                var buildingMap = new Dictionary<ulong, BuildingObject>();
                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                
                await using (var reader = await cmd.ExecuteReaderAsync(ct)) {
                    while (await reader.ReadAsync(ct)) {
                        var bldg = new BuildingObject {
                            ModelId = (uint)reader.GetInt64(0),
                            Position = new System.Numerics.Vector3(reader.GetFloat(1), reader.GetFloat(2), reader.GetFloat(3)),
                            Rotation = new System.Numerics.Quaternion(reader.GetFloat(5), reader.GetFloat(6), reader.GetFloat(7), reader.GetFloat(4)),
                            InstanceId = (ulong)reader.GetInt64(8),
                            LayerId = reader.GetString(9),
                            NumLeaves = (uint)reader.GetInt64(10),
                            IsDeleted = reader.GetBoolean(11),
                            Portals = new List<WbBuildingPortal>()
                        };
                        var lbId = (uint)reader.GetInt64(12);
                        buildingMap[bldg.InstanceId] = bldg;
                        
                        if (!results.TryGetValue(lbId, out var list)) {
                            list = new List<BuildingObject>();
                            results[lbId] = list;
                        }
                        list.Add(bldg);
                    }
                }

                if (buildingMap.Count > 0) {
                    var instanceIds = string.Join(",", buildingMap.Keys);
                    var portalSql = $"SELECT Id, InstanceId, Flags, OtherCellId, OtherPortalId FROM BuildingPortals WHERE InstanceId IN ({instanceIds})";
                    var portalsById = new Dictionary<long, WbBuildingPortal>();

                    await using (var portalCmd = Connection.CreateCommand()) {
                        portalCmd.Transaction = dbTx;
                        portalCmd.CommandText = portalSql;
                        await using var portalReader = await portalCmd.ExecuteReaderAsync(ct);
                        while (await portalReader.ReadAsync(ct)) {
                            var id = portalReader.GetInt64(0);
                            var instId = (ulong)portalReader.GetInt64(1);
                            var portal = new WbBuildingPortal {
                                Flags = (uint)portalReader.GetInt64(2),
                                OtherCellId = (ushort)portalReader.GetInt32(3),
                                OtherPortalId = (ushort)portalReader.GetInt32(4),
                                StabList = new List<ushort>()
                            };
                            portalsById[id] = portal;
                            if (buildingMap.TryGetValue(instId, out var bldg)) {
                                bldg.Portals.Add(portal);
                            }
                        }
                    }

                    if (portalsById.Count > 0) {
                        var portalIds = string.Join(",", portalsById.Keys);
                        var stabSql = $"SELECT PortalId, StabId FROM BuildingPortalStabs WHERE PortalId IN ({portalIds})";
                        await using (var stabCmd = Connection.CreateCommand()) {
                            stabCmd.Transaction = dbTx;
                            stabCmd.CommandText = stabSql;
                            await using var stabReader = await stabCmd.ExecuteReaderAsync(ct);
                            while (await stabReader.ReadAsync(ct)) {
                                var portalId = stabReader.GetInt64(0);
                                var stabId = (ushort)stabReader.GetInt32(1);
                                if (portalsById.TryGetValue(portalId, out var portal)) {
                                    portal.StabList.Add(stabId);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error retrieving buildings for multiple landblocks");
            }
            return results.ToDictionary(k => k.Key, v => (IReadOnlyList<BuildingObject>)v.Value);
        }

        /// <inheritdoc/>
        public async Task<Result<Unit>> UpsertBuildingAsync(BuildingObject obj, uint regionId, uint? landblockId, ITransaction? tx, CancellationToken ct) {
            try {
                var dbTxResult = GetDbTransaction(tx);
                if (dbTxResult.IsFailure) return Result<Unit>.Failure(dbTxResult.Error);
                var dbTx = dbTxResult.Value;

                const string sql = @"
                    INSERT INTO Buildings (InstanceId, RegionId, LayerId, LandblockId, ModelId, PosX, PosY, PosZ, RotW, RotX, RotY, RotZ, NumLeaves, IsDeleted)
                    VALUES (@id, @regionId, @layerId, @lbId, @modelId, @px, @py, @pz, @rw, @rx, @ry, @rz, @nl, 0)
                    ON CONFLICT(InstanceId) DO UPDATE SET
                        RegionId = @regionId,
                        LayerId = @layerId,
                        LandblockId = @lbId,
                        ModelId = @modelId,
                        PosX = @px, PosY = @py, PosZ = @pz,
                        RotW = @rw, RotX = @rx, RotY = @ry, RotZ = @rz,
                        NumLeaves = @nl,
                        IsDeleted = 0";

                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", (long)obj.InstanceId);
                cmd.Parameters.AddWithValue("@regionId", (long)regionId);
                cmd.Parameters.AddWithValue("@layerId", obj.LayerId);
                cmd.Parameters.AddWithValue("@lbId", (object?)landblockId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@modelId", (long)obj.ModelId);
                cmd.Parameters.AddWithValue("@px", obj.Position.X);
                cmd.Parameters.AddWithValue("@py", obj.Position.Y);
                cmd.Parameters.AddWithValue("@pz", obj.Position.Z);
                cmd.Parameters.AddWithValue("@rw", obj.Rotation.W);
                cmd.Parameters.AddWithValue("@rx", obj.Rotation.X);
                cmd.Parameters.AddWithValue("@ry", obj.Rotation.Y);
                cmd.Parameters.AddWithValue("@rz", obj.Rotation.Z);
                cmd.Parameters.AddWithValue("@nl", (long)obj.NumLeaves);
                await cmd.ExecuteNonQueryAsync(ct);

                // Remove existing portals and stabs (cascade takes care of stabs if we delete portals, or we delete both)
                await using (var delCmd = Connection.CreateCommand()) {
                    delCmd.Transaction = dbTx;
                    delCmd.CommandText = "DELETE FROM BuildingPortals WHERE InstanceId = @id";
                    delCmd.Parameters.AddWithValue("@id", (long)obj.InstanceId);
                    await delCmd.ExecuteNonQueryAsync(ct);
                }

                if (obj.Portals != null) {
                    foreach (var portal in obj.Portals) {
                        long portalId;
                        await using (var pCmd = Connection.CreateCommand()) {
                            pCmd.Transaction = dbTx;
                            pCmd.CommandText = "INSERT INTO BuildingPortals (InstanceId, Flags, OtherCellId, OtherPortalId) VALUES (@inst, @flags, @oc, @op) RETURNING Id;";
                            pCmd.Parameters.AddWithValue("@inst", (long)obj.InstanceId);
                            pCmd.Parameters.AddWithValue("@flags", (long)portal.Flags);
                            pCmd.Parameters.AddWithValue("@oc", (int)portal.OtherCellId);
                            pCmd.Parameters.AddWithValue("@op", (int)portal.OtherPortalId);
                            var pidObj = await pCmd.ExecuteScalarAsync(ct);
                            portalId = Convert.ToInt64(pidObj);
                        }

                        if (portal.StabList != null) {
                            foreach (var stab in portal.StabList) {
                                await using (var sCmd = Connection.CreateCommand()) {
                                    sCmd.Transaction = dbTx;
                                    sCmd.CommandText = "INSERT INTO BuildingPortalStabs (PortalId, StabId) VALUES (@pid, @sid)";
                                    sCmd.Parameters.AddWithValue("@pid", portalId);
                                    sCmd.Parameters.AddWithValue("@sid", (int)stab);
                                    await sCmd.ExecuteNonQueryAsync(ct);
                                }
                            }
                        }
                    }
                }

                return Result<Unit>.Success(Unit.Value);
            }
            catch (Exception ex) {
                return Result<Unit>.Failure($"Error upserting building: {ex.Message}", "DATABASE_ERROR");
            }
        }

        /// <inheritdoc/>
        public async Task<Result<Cell>> GetEnvCellAsync(uint cellId, ITransaction? tx, CancellationToken ct) {
            try {
                var dbTxResult = GetDbTransaction(tx);
                if (dbTxResult.IsFailure) return Result<Cell>.Failure(dbTxResult.Error);
                var dbTx = dbTxResult.Value;

                const string sql = "SELECT EnvironmentId, Flags, CellStructure, PosX, PosY, PosZ, RotW, RotX, RotY, RotZ, RestrictionObj, LayerId, MinX, MinY, MinZ, MaxX, MaxY, MaxZ FROM EnvCells WHERE CellId = @id";
                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", (long)cellId);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct)) {
                    var cell = new Cell {
                        EnvironmentId = (ushort)reader.GetInt32(0),
                        Flags = (uint)reader.GetInt64(1),
                        CellStructure = (ushort)reader.GetInt32(2),
                        Position = new System.Numerics.Vector3(reader.GetFloat(3), reader.GetFloat(4), reader.GetFloat(5)),
                        Rotation = new System.Numerics.Quaternion(reader.GetFloat(7), reader.GetFloat(8), reader.GetFloat(9), reader.GetFloat(6)),
                        RestrictionObj = (uint)reader.GetInt64(10),
                        LayerId = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                        MinBounds = new System.Numerics.Vector3(reader.GetFloat(12), reader.GetFloat(13), reader.GetFloat(14)),
                        MaxBounds = new System.Numerics.Vector3(reader.GetFloat(15), reader.GetFloat(16), reader.GetFloat(17)),
                        Surfaces = new List<ushort>(),
                        Portals = new List<WbCellPortal>(),
                        VisibleCells = new List<ushort>()
                    };
                    
                    await reader.CloseAsync();

                    // Surfaces
                    await using (var surfCmd = Connection.CreateCommand()) {
                        surfCmd.Transaction = dbTx;
                        surfCmd.CommandText = "SELECT SurfaceId FROM EnvCellSurfaces WHERE CellId = @id";
                        surfCmd.Parameters.AddWithValue("@id", (long)cellId);
                        await using var surfReader = await surfCmd.ExecuteReaderAsync(ct);
                        while (await surfReader.ReadAsync(ct)) {
                            cell.Surfaces.Add((ushort)surfReader.GetInt32(0));
                        }
                    }

                    // Portals
                    await using (var portCmd = Connection.CreateCommand()) {
                        portCmd.Transaction = dbTx;
                        portCmd.CommandText = "SELECT Flags, PolygonId, OtherCellId, OtherPortalId FROM EnvCellPortals WHERE CellId = @id";
                        portCmd.Parameters.AddWithValue("@id", (long)cellId);
                        await using var portReader = await portCmd.ExecuteReaderAsync(ct);
                        while (await portReader.ReadAsync(ct)) {
                            cell.Portals.Add(new WbCellPortal {
                                Flags = (uint)portReader.GetInt64(0),
                                PolygonId = (ushort)portReader.GetInt32(1),
                                OtherCellId = (ushort)portReader.GetInt32(2),
                                OtherPortalId = (ushort)portReader.GetInt32(3)
                            });
                        }
                    }

                    // VisibleCells
                    await using (var visCmd = Connection.CreateCommand()) {
                        visCmd.Transaction = dbTx;
                        visCmd.CommandText = "SELECT VisibleCellId FROM EnvCellVisibleCells WHERE CellId = @id";
                        visCmd.Parameters.AddWithValue("@id", (long)cellId);
                        await using var visReader = await visCmd.ExecuteReaderAsync(ct);
                        while (await visReader.ReadAsync(ct)) {
                            cell.VisibleCells.Add((ushort)visReader.GetInt32(0));
                        }
                    }

                    return Result<Cell>.Success(cell);
                }
                return Result<Cell>.Failure($"EnvCell not found: {cellId}", "NOT_FOUND");
            }
            catch (Exception ex) {
                return Result<Cell>.Failure($"Error getting env cell: {ex.Message}", "DATABASE_ERROR");
            }
        }

        /// <inheritdoc/>
        public async Task<Result<Unit>> UpsertEnvCellAsync(uint cellId, uint regionId, Cell cell, ITransaction? tx, CancellationToken ct) {
            try {
                var dbTxResult = GetDbTransaction(tx);
                if (dbTxResult.IsFailure) return Result<Unit>.Failure(dbTxResult.Error);
                var dbTx = dbTxResult.Value;

                const string sql = @"
                    INSERT INTO EnvCells (CellId, RegionId, LayerId, EnvironmentId, Flags, CellStructure, PosX, PosY, PosZ, RotW, RotX, RotY, RotZ, RestrictionObj, MinX, MinY, MinZ, MaxX, MaxY, MaxZ, Version)
                    VALUES (@id, @regionId, @layerId, @envId, @flags, @struct, @px, @py, @pz, @rw, @rx, @ry, @rz, @restr, @minx, @miny, @minz, @maxx, @maxy, @maxz, @version)
                    ON CONFLICT(CellId) DO UPDATE SET
                        EnvironmentId = @envId,
                        Flags = @flags,
                        CellStructure = @struct,
                        PosX = @px, PosY = @py, PosZ = @pz,
                        RotW = @rw, RotX = @rx, RotY = @ry, RotZ = @rz,
                        RestrictionObj = @restr,
                        MinX = @minx, MinY = @miny, MinZ = @minz,
                        MaxX = @maxx, MaxY = @maxy, MaxZ = @maxz,
                        Version = Version + 1";

                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", (long)cellId);
                cmd.Parameters.AddWithValue("@regionId", (long)regionId);
                cmd.Parameters.AddWithValue("@layerId", cell.LayerId);
                cmd.Parameters.AddWithValue("@envId", (int)cell.EnvironmentId);
                cmd.Parameters.AddWithValue("@flags", (long)cell.Flags);
                cmd.Parameters.AddWithValue("@struct", (int)cell.CellStructure);
                cmd.Parameters.AddWithValue("@px", cell.Position.X);
                cmd.Parameters.AddWithValue("@py", cell.Position.Y);
                cmd.Parameters.AddWithValue("@pz", cell.Position.Z);
                cmd.Parameters.AddWithValue("@rw", cell.Rotation.W);
                cmd.Parameters.AddWithValue("@rx", cell.Rotation.X);
                cmd.Parameters.AddWithValue("@ry", cell.Rotation.Y);
                cmd.Parameters.AddWithValue("@rz", cell.Rotation.Z);
                cmd.Parameters.AddWithValue("@restr", (long)cell.RestrictionObj);
                cmd.Parameters.AddWithValue("@minx", cell.MinBounds.X);
                cmd.Parameters.AddWithValue("@miny", cell.MinBounds.Y);
                cmd.Parameters.AddWithValue("@minz", cell.MinBounds.Z);
                cmd.Parameters.AddWithValue("@maxx", cell.MaxBounds.X);
                cmd.Parameters.AddWithValue("@maxy", cell.MaxBounds.Y);
                cmd.Parameters.AddWithValue("@maxz", cell.MaxBounds.Z);
                cmd.Parameters.AddWithValue("@version", 1);
                await cmd.ExecuteNonQueryAsync(ct);

                // Delete existing child records
                await using (var delCmd = Connection.CreateCommand()) {
                    delCmd.Transaction = dbTx;
                    delCmd.CommandText = "DELETE FROM EnvCellSurfaces WHERE CellId = @id; DELETE FROM EnvCellPortals WHERE CellId = @id; DELETE FROM EnvCellVisibleCells WHERE CellId = @id;";
                    delCmd.Parameters.AddWithValue("@id", (long)cellId);
                    await delCmd.ExecuteNonQueryAsync(ct);
                }

                // Insert Surfaces
                if (cell.Surfaces != null) {
                    foreach (var surface in cell.Surfaces) {
                        await using (var sCmd = Connection.CreateCommand()) {
                            sCmd.Transaction = dbTx;
                            sCmd.CommandText = "INSERT INTO EnvCellSurfaces (CellId, SurfaceId) VALUES (@id, @surf)";
                            sCmd.Parameters.AddWithValue("@id", (long)cellId);
                            sCmd.Parameters.AddWithValue("@surf", (int)surface);
                            await sCmd.ExecuteNonQueryAsync(ct);
                        }
                    }
                }

                // Insert Portals
                if (cell.Portals != null) {
                    foreach (var portal in cell.Portals) {
                        await using (var pCmd = Connection.CreateCommand()) {
                            pCmd.Transaction = dbTx;
                            pCmd.CommandText = "INSERT INTO EnvCellPortals (CellId, Flags, PolygonId, OtherCellId, OtherPortalId) VALUES (@id, @flags, @poly, @oc, @op)";
                            pCmd.Parameters.AddWithValue("@id", (long)cellId);
                            pCmd.Parameters.AddWithValue("@flags", (long)portal.Flags);
                            pCmd.Parameters.AddWithValue("@poly", (int)portal.PolygonId);
                            pCmd.Parameters.AddWithValue("@oc", (int)portal.OtherCellId);
                            pCmd.Parameters.AddWithValue("@op", (int)portal.OtherPortalId);
                            await pCmd.ExecuteNonQueryAsync(ct);
                        }
                    }
                }

                // Insert VisibleCells
                if (cell.VisibleCells != null) {
                    foreach (var vc in cell.VisibleCells) {
                        await using (var vCmd = Connection.CreateCommand()) {
                            vCmd.Transaction = dbTx;
                            vCmd.CommandText = "INSERT INTO EnvCellVisibleCells (CellId, VisibleCellId) VALUES (@id, @vc)";
                            vCmd.Parameters.AddWithValue("@id", (long)cellId);
                            vCmd.Parameters.AddWithValue("@vc", (int)vc);
                            await vCmd.ExecuteNonQueryAsync(ct);
                        }
                    }
                }

                return Result<Unit>.Success(Unit.Value);
            }
            catch (Exception ex) {
                return Result<Unit>.Failure($"Error upserting env cell: {ex.Message}", "DATABASE_ERROR");
            }
        }

        public async Task<Result<Unit>> DeleteStaticObjectAsync(ulong instanceId, ITransaction? tx, CancellationToken ct) {
            try {
                var dbTxResult = GetDbTransaction(tx);
                if (dbTxResult.IsFailure) return Result<Unit>.Failure(dbTxResult.Error);
                var dbTx = dbTxResult.Value;

                const string sql = "UPDATE StaticObjects SET IsDeleted = 1 WHERE InstanceId = @id";
                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", (long)instanceId);
                await cmd.ExecuteNonQueryAsync(ct);
                return Result<Unit>.Success(Unit.Value);
            }
            catch (Exception ex) {
                return Result<Unit>.Failure($"Error deleting static object: {ex.Message}", "DATABASE_ERROR");
            }
        }

        public async Task<Result<Unit>> DeleteBuildingAsync(ulong instanceId, ITransaction? tx, CancellationToken ct) {
            try {
                var dbTxResult = GetDbTransaction(tx);
                if (dbTxResult.IsFailure) return Result<Unit>.Failure(dbTxResult.Error);
                var dbTx = dbTxResult.Value;

                const string sql = "UPDATE Buildings SET IsDeleted = 1 WHERE InstanceId = @id";
                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", (long)instanceId);
                await cmd.ExecuteNonQueryAsync(ct);
                return Result<Unit>.Success(Unit.Value);
            }
            catch (Exception ex) {
                return Result<Unit>.Failure($"Error deleting building: {ex.Message}", "DATABASE_ERROR");
            }
        }

        private bool _disposed;

        /// <inheritdoc/>
        public async ValueTask DisposeAsync() {
            if (_disposed) return;
            _disposed = true;
            _logger?.LogInformation("Disposing SQLiteProjectRepository asynchronously");
            try {
                if (Connection != null) {
                    await Connection.DisposeAsync();
                }
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error disposing SQLite connection");
            }
        }

        /// <inheritdoc/>
        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            _logger?.LogInformation("Disposing SQLiteProjectRepository");
            try {
                Connection?.Dispose();
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error disposing SQLite connection");
            }
        }
    }
}