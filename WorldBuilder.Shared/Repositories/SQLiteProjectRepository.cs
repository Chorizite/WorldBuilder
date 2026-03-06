using FluentMigrator.Runner;
using MemoryPack;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Initializes a new instance of the <see cref="SQLiteProjectRepository"/> class.
        /// </summary>
        /// <param name="connectionString">The SQLite connection string.</param>
        /// <param name="logger">The logger (optional).</param>
        public SQLiteProjectRepository(string connectionString, ILogger<SQLiteProjectRepository>? logger = null) {
            Connection = new SqliteConnection(connectionString);
            Connection.Open();

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
        public async Task<IReadOnlyList<StaticObject>> GetStaticObjectsAsync(uint? landblockId, uint? cellId, ITransaction? tx, CancellationToken ct) {
            var objects = new List<StaticObject>();
            try {
                var dbTxResult = GetDbTransaction(tx);
                var dbTx = dbTxResult.IsSuccess ? dbTxResult.Value : null;

                var sql = "SELECT InstanceId, ModelId, PosX, PosY, PosZ, RotW, RotX, RotY, RotZ, LayerId FROM StaticObjects WHERE IsDeleted = 0";
                if (landblockId != null) sql += " AND LandblockId = @lbId";
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
                        Position = [reader.GetFloat(2), reader.GetFloat(3), reader.GetFloat(4), reader.GetFloat(5), reader.GetFloat(6), reader.GetFloat(7), reader.GetFloat(8)],
                        LayerId = reader.GetString(9)
                    });
                }
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error retrieving static objects for landblock {LandblockId}", landblockId);
            }
            return objects;
        }

        /// <inheritdoc/>
        public async Task<Result<Unit>> UpsertStaticObjectAsync(StaticObject obj, uint regionId, uint? landblockId, uint? cellId, ITransaction? tx, CancellationToken ct) {
            try {
                var dbTxResult = GetDbTransaction(tx);
                if (dbTxResult.IsFailure) return Result<Unit>.Failure(dbTxResult.Error);
                var dbTx = dbTxResult.Value;

                const string sql = @"
                    INSERT INTO StaticObjects (InstanceId, RegionId, LayerId, LandblockId, CellId, ModelId, PosX, PosY, PosZ, RotW, RotX, RotY, RotZ, IsDeleted)
                    VALUES (@id, @regionId, @layerId, @lbId, @cellId, @modelId, @px, @py, @pz, @rw, @rx, @ry, @rz, 0)
                    ON CONFLICT(InstanceId) DO UPDATE SET
                        RegionId = @regionId,
                        LayerId = @layerId,
                        LandblockId = @lbId,
                        CellId = @cellId,
                        ModelId = @modelId,
                        PosX = @px, PosY = @py, PosZ = @pz,
                        RotW = @rw, RotX = @rx, RotY = @ry, RotZ = @rz,
                        IsDeleted = 0";

                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", (long)obj.InstanceId);
                cmd.Parameters.AddWithValue("@regionId", (long)regionId);
                cmd.Parameters.AddWithValue("@layerId", obj.LayerId);
                cmd.Parameters.AddWithValue("@lbId", (object?)landblockId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@cellId", (object?)cellId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@modelId", (long)obj.SetupId);
                cmd.Parameters.AddWithValue("@px", obj.Position.Length > 0 ? obj.Position[0] : 0f);
                cmd.Parameters.AddWithValue("@py", obj.Position.Length > 1 ? obj.Position[1] : 0f);
                cmd.Parameters.AddWithValue("@pz", obj.Position.Length > 2 ? obj.Position[2] : 0f);
                cmd.Parameters.AddWithValue("@rw", obj.Position.Length > 3 ? obj.Position[3] : 1f);
                cmd.Parameters.AddWithValue("@rx", obj.Position.Length > 4 ? obj.Position[4] : 0f);
                cmd.Parameters.AddWithValue("@ry", obj.Position.Length > 5 ? obj.Position[5] : 0f);
                cmd.Parameters.AddWithValue("@rz", obj.Position.Length > 6 ? obj.Position[6] : 0f);
                await cmd.ExecuteNonQueryAsync(ct);
                return Result<Unit>.Success(Unit.Value);
            }
            catch (Exception ex) {
                return Result<Unit>.Failure($"Error upserting static object: {ex.Message}", "DATABASE_ERROR");
            }
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<BuildingObject>> GetBuildingsAsync(uint? landblockId, uint? cellId, ITransaction? tx, CancellationToken ct) {
            var sql = @"
                SELECT ModelId, PosX, PosY, PosZ, RotW, RotX, RotY, RotZ, InstanceId, LayerId
                FROM Buildings 
                WHERE IsDeleted = 0";
            if (landblockId != null) sql += " AND LandblockId = @lbId";
            if (cellId != null) sql += " AND CellId = @cellId";

            var results = new List<BuildingObject>();
            try {
                var dbTxResult = GetDbTransaction(tx);
                var dbTx = dbTxResult.IsSuccess ? dbTxResult.Value : null;

                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                if (landblockId != null) cmd.Parameters.AddWithValue("@lbId", (long)landblockId);
                if (cellId != null) cmd.Parameters.AddWithValue("@cellId", (long)cellId);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) {
                    results.Add(new BuildingObject {
                        ModelId = (uint)reader.GetInt64(0),
                        Position = new float[] {
                            reader.GetFloat(1), reader.GetFloat(2), reader.GetFloat(3),
                            reader.GetFloat(4), reader.GetFloat(5), reader.GetFloat(6), reader.GetFloat(7)
                        },
                        InstanceId = (ulong)reader.GetInt64(8),
                        LayerId = reader.GetString(9)
                    });
                }
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error getting buildings for landblock {LandblockId}", landblockId);
            }
            return results;
        }

        /// <inheritdoc/>
        public async Task<Result<Unit>> UpsertBuildingAsync(BuildingObject obj, uint regionId, uint? landblockId, uint? cellId, ITransaction? tx, CancellationToken ct) {
            try {
                var dbTxResult = GetDbTransaction(tx);
                if (dbTxResult.IsFailure) return Result<Unit>.Failure(dbTxResult.Error);
                var dbTx = dbTxResult.Value;

                const string sql = @"
                    INSERT INTO Buildings (InstanceId, RegionId, LayerId, LandblockId, CellId, ModelId, PosX, PosY, PosZ, RotW, RotX, RotY, RotZ, IsDeleted)
                    VALUES (@id, @regionId, @layerId, @lbId, @cellId, @modelId, @px, @py, @pz, @rw, @rx, @ry, @rz, 0)
                    ON CONFLICT(InstanceId) DO UPDATE SET
                        RegionId = @regionId,
                        LayerId = @layerId,
                        LandblockId = @lbId,
                        CellId = @cellId,
                        ModelId = @modelId,
                        PosX = @px, PosY = @py, PosZ = @pz,
                        RotW = @rw, RotX = @rx, RotY = @ry, RotZ = @rz,
                        IsDeleted = 0";

                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", (long)obj.InstanceId);
                cmd.Parameters.AddWithValue("@regionId", (long)regionId);
                cmd.Parameters.AddWithValue("@layerId", obj.LayerId);
                cmd.Parameters.AddWithValue("@lbId", (object?)landblockId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@cellId", (object?)cellId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@modelId", (long)obj.ModelId);
                cmd.Parameters.AddWithValue("@px", obj.Position.Length > 0 ? obj.Position[0] : 0f);
                cmd.Parameters.AddWithValue("@py", obj.Position.Length > 1 ? obj.Position[1] : 0f);
                cmd.Parameters.AddWithValue("@pz", obj.Position.Length > 2 ? obj.Position[2] : 0f);
                cmd.Parameters.AddWithValue("@rw", obj.Position.Length > 3 ? obj.Position[3] : 1f);
                cmd.Parameters.AddWithValue("@rx", obj.Position.Length > 4 ? obj.Position[4] : 0f);
                cmd.Parameters.AddWithValue("@ry", obj.Position.Length > 5 ? obj.Position[5] : 0f);
                cmd.Parameters.AddWithValue("@rz", obj.Position.Length > 6 ? obj.Position[6] : 0f);
                await cmd.ExecuteNonQueryAsync(ct);
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

                const string sql = "SELECT EnvironmentId, Flags, Data FROM EnvCells WHERE CellId = @id";
                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", (long)cellId);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct)) {
                    var cell = new Cell {
                        EnvironmentId = (ushort)reader.GetInt32(0),
                        Flags = (uint)reader.GetInt64(1)
                    };
                    if (!reader.IsDBNull(2)) {
                        var data = (byte[])reader.GetValue(2);
                        var extra = MemoryPackSerializer.Deserialize<Cell>(data);
                        if (extra != null) {
                            // Merge extra data (StaticObjects, etc)
                            foreach (var obj in extra.StaticObjects) {
                                cell.StaticObjects[obj.Key] = obj.Value;
                            }
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

                var data = MemoryPackSerializer.Serialize(cell);

                const string sql = @"
                    INSERT INTO EnvCells (CellId, RegionId, LayerId, EnvironmentId, Flags, Data, Version)
                    VALUES (@id, @regionId, @layerId, @envId, @flags, @data, @version)
                    ON CONFLICT(CellId) DO UPDATE SET
                        EnvironmentId = @envId,
                        Flags = @flags,
                        Data = @data,
                        Version = Version + 1";

                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", (long)cellId);
                cmd.Parameters.AddWithValue("@regionId", (long)regionId);
                cmd.Parameters.AddWithValue("@layerId", cell.LayerId);
                cmd.Parameters.AddWithValue("@envId", (int)cell.EnvironmentId);
                cmd.Parameters.AddWithValue("@flags", (long)cell.Flags);
                cmd.Parameters.AddWithValue("@data", data);
                cmd.Parameters.AddWithValue("@version", 1);
                await cmd.ExecuteNonQueryAsync(ct);
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

        /// <inheritdoc/>
        public void Dispose() {
            _logger?.LogInformation("Disposing SQLiteProjectRepository");
            Connection?.Close();
        }
    }
}