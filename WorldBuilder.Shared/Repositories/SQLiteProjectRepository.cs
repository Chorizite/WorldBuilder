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
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Lib.Extensions;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Migrations;

namespace WorldBuilder.Shared.Repositories {
    /// <summary>
    /// A SQLite-based implementation of <see cref="IProjectRepository"/>.
    /// </summary>
    public class SQLiteProjectRepository : IProjectRepository {
        private readonly ILogger<SQLiteProjectRepository>? _logger;
        private readonly string _connectionString;
        private bool _disposed;

        /// <inheritdoc/>
        public string ProjectDirectory { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SQLiteProjectRepository"/> class.
        /// </summary>
        public SQLiteProjectRepository(string connectionString, ILoggerFactory? loggerFactory = null) {
            _logger = loggerFactory?.CreateLogger<SQLiteProjectRepository>();
            _connectionString = connectionString;
            
            // Extract directory from connection string if possible
            var builder = new SqliteConnectionStringBuilder(connectionString);
            if (!string.IsNullOrEmpty(builder.DataSource) && !builder.DataSource.StartsWith(":memory:", StringComparison.OrdinalIgnoreCase)) {
                ProjectDirectory = System.IO.Path.GetDirectoryName(builder.DataSource) ?? string.Empty;
            } else {
                ProjectDirectory = string.Empty;
            }

            // Enable WAL mode for better performance and concurrency
            using (var connection = new SqliteConnection(_connectionString)) {
                connection.Open();
                using (var cmd = connection.CreateCommand()) {
                    cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <inheritdoc/>
        public Task InitializeDatabaseAsync(CancellationToken ct) {
            _logger?.LogTrace("Initializing database");
            
            var serviceProvider = new ServiceCollection()
                .AddFluentMigratorCore()
                .ConfigureRunner(rb => rb
                    .AddSQLite()
                    .WithGlobalConnectionString(_connectionString)
                    .ScanIn(typeof(Migration_001_InitialSchema).Assembly).For.Migrations())
                .BuildServiceProvider(false);

            using var scope = serviceProvider.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
            runner.MigrateUp();
            _logger?.LogTrace("Database initialized successfully");
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task<ITransaction> CreateTransactionAsync(CancellationToken ct) {
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);
            await ConfigureConnectionAsync(connection, ct);
            var dbTransaction = await connection.BeginTransactionAsync(ct);
            return new DatabaseTransactionAdapter(dbTransaction, connection);
        }

        private async Task ConfigureConnectionAsync(SqliteConnection connection, CancellationToken ct) {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
            await cmd.ExecuteNonQueryAsync(ct);
        }

        private async Task<T> ExecuteAsync<T>(ITransaction? tx, Func<SqliteConnection, SqliteTransaction?, Task<T>> action, CancellationToken ct) {
            var sqliteTx = GetSqliteTransaction(tx);
            if (sqliteTx != null) {
                return await action((SqliteConnection)sqliteTx.Connection!, sqliteTx);
            } else {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync(ct);
                await ConfigureConnectionAsync(connection, ct);
                return await action(connection, null);
            }
        }

        private SqliteTransaction? GetSqliteTransaction(ITransaction? tx) {
            if (tx is DatabaseTransactionAdapter adapter) {
                return adapter.UnderlyingTransaction as SqliteTransaction;
            }
            return null;
        }

        public async Task<IReadOnlyList<LandscapeLayerBase>> GetLayersAsync(uint regionId, ITransaction? tx, CancellationToken ct) {
            return await ExecuteAsync(tx, async (connection, sqliteTx) => {
                var items = new List<LandscapeLayerBase>();

                // 1. Get Groups
                var sqlGroups = "SELECT Id, Name, ParentId, IsExported, SortOrder FROM LandscapeGroups WHERE RegionId = @regionId ORDER BY SortOrder ASC";
                using (var cmd = new SqliteCommand(sqlGroups, connection, sqliteTx)) {
                    cmd.Parameters.AddWithValue("@regionId", regionId);
                    using var reader = await cmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct)) {
                        items.Add(new LandscapeLayerGroup(reader.GetString(0)) {
                            Name = reader.GetString(1),
                            ParentId = reader.IsDBNull(2) ? null : reader.GetString(2),
                            IsExported = reader.GetBoolean(3)
                        });
                    }
                }

                // 2. Get Layers
                var sqlLayers = "SELECT Id, Name, ParentId, IsExported, IsBase, SortOrder FROM LandscapeLayers WHERE RegionId = @regionId ORDER BY SortOrder ASC";
                using (var cmd = new SqliteCommand(sqlLayers, connection, sqliteTx)) {
                    cmd.Parameters.AddWithValue("@regionId", regionId);
                    using var reader = await cmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct)) {
                        items.Add(new LandscapeLayer(reader.GetString(0), reader.GetBoolean(4)) {
                            Name = reader.GetString(1),
                            ParentId = reader.IsDBNull(2) ? null : reader.GetString(2),
                            IsExported = reader.GetBoolean(3)
                        });
                    }
                }

                return (IReadOnlyList<LandscapeLayerBase>)items;
            }, ct);
        }

        public async Task<Result<Unit>> UpsertLayerAsync(LandscapeLayerBase item, uint regionId, int sortOrder, ITransaction? tx, CancellationToken ct) {
            return await ExecuteAsync(tx, async (connection, sqliteTx) => {
                if (item is LandscapeLayer layer) {
                    var sql = @"INSERT INTO LandscapeLayers (Id, RegionId, Name, ParentId, IsExported, IsBase, SortOrder) 
                                VALUES (@id, @regionId, @name, @parentId, @isExported, @isBase, @sortOrder)
                                ON CONFLICT(Id) DO UPDATE SET Name = @name, ParentId = @parentId, IsExported = @isExported, SortOrder = @sortOrder";
                    using var cmd = new SqliteCommand(sql, connection, sqliteTx);
                    cmd.Parameters.AddWithValue("@id", layer.Id);
                    cmd.Parameters.AddWithValue("@regionId", regionId);
                    cmd.Parameters.AddWithValue("@name", layer.Name);
                    cmd.Parameters.AddWithValue("@parentId", (object?)layer.ParentId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@isExported", layer.IsExported);
                    cmd.Parameters.AddWithValue("@isBase", layer.IsBase);
                    cmd.Parameters.AddWithValue("@sortOrder", sortOrder);
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                else if (item is LandscapeLayerGroup group) {
                    var sql = @"INSERT INTO LandscapeGroups (Id, RegionId, Name, ParentId, IsExported, SortOrder) 
                                VALUES (@id, @regionId, @name, @parentId, @isExported, @sortOrder)
                                ON CONFLICT(Id) DO UPDATE SET Name = @name, ParentId = @parentId, IsExported = @isExported, SortOrder = @sortOrder";
                    using var cmd = new SqliteCommand(sql, connection, sqliteTx);
                    cmd.Parameters.AddWithValue("@id", group.Id);
                    cmd.Parameters.AddWithValue("@regionId", regionId);
                    cmd.Parameters.AddWithValue("@name", group.Name);
                    cmd.Parameters.AddWithValue("@parentId", (object?)group.ParentId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@isExported", group.IsExported);
                    cmd.Parameters.AddWithValue("@sortOrder", sortOrder);
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                return Result<Unit>.Success(Unit.Value);
            }, ct);
        }

        public async Task<Result<Unit>> DeleteLayerAsync(string id, ITransaction? tx, CancellationToken ct) {
            return await ExecuteAsync(tx, async (connection, sqliteTx) => {
                using var cmd = new SqliteCommand("DELETE FROM LandscapeLayers WHERE Id = @id; DELETE FROM LandscapeGroups WHERE Id = @id;", connection, sqliteTx);
                cmd.Parameters.AddWithValue("@id", id);
                await cmd.ExecuteNonQueryAsync(ct);
                return Result<Unit>.Success(Unit.Value);
            }, ct);
        }

        public async Task<IReadOnlyList<StaticObject>> GetStaticObjectsAsync(ushort? landblockId, uint? cellId, ITransaction? tx, CancellationToken ct) {
            return await ExecuteAsync(tx, async (connection, sqliteTx) => {
                var sql = "SELECT InstanceId, ModelId, LayerId, PosX, PosY, PosZ, RotW, RotX, RotY, RotZ, IsDeleted, CellId FROM StaticObjects WHERE 1=1";
                if (landblockId.HasValue) sql += " AND LandblockId = @lbId";
                if (cellId.HasValue) sql += " AND CellId = @cellId";

                using var cmd = new SqliteCommand(sql, connection, sqliteTx);
                if (landblockId.HasValue) cmd.Parameters.AddWithValue("@lbId", (int)landblockId.Value);
                if (cellId.HasValue) cmd.Parameters.AddWithValue("@cellId", (long)cellId.Value);

                var results = new List<StaticObject>();
                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) {
                    results.Add(new StaticObject {
                        InstanceId = ObjectId.Parse(reader.GetString(0)),
                        ModelId = (uint)reader.GetInt32(1),
                        LayerId = reader.GetString(2),
                        Position = new Vector3(reader.GetFloat(3), reader.GetFloat(4), reader.GetFloat(5)),
                        Rotation = new Quaternion(reader.GetFloat(7), reader.GetFloat(8), reader.GetFloat(9), reader.GetFloat(6)),
                        IsDeleted = reader.GetBoolean(10),
                        CellId = reader.IsDBNull(11) ? null : (uint?)reader.GetInt64(11)
                    });
                }
                return (IReadOnlyList<StaticObject>)results;
            }, ct);
        }

        public async Task<IReadOnlyDictionary<ushort, IReadOnlyList<StaticObject>>> GetStaticObjectsForLandblocksAsync(IEnumerable<ushort> landblockIds, ITransaction? tx, CancellationToken ct) {
            return await ExecuteAsync(tx, async (connection, sqliteTx) => {
                var ids = landblockIds.ToList();
                if (ids.Count == 0) return (IReadOnlyDictionary<ushort, IReadOnlyList<StaticObject>>)new Dictionary<ushort, IReadOnlyList<StaticObject>>();

                var results = ids.ToDictionary(id => id, _ => (IReadOnlyList<StaticObject>)new List<StaticObject>());
                var idString = string.Join(",", ids.Select(id => id.ToString()));
                var sql = $"SELECT LandblockId, InstanceId, ModelId, LayerId, PosX, PosY, PosZ, RotW, RotX, RotY, RotZ, IsDeleted, CellId FROM StaticObjects WHERE LandblockId IN ({idString})";

                using var cmd = new SqliteCommand(sql, connection, sqliteTx);
                using var reader = await cmd.ExecuteReaderAsync(ct);
                var temp = new Dictionary<ushort, List<StaticObject>>();
                while (await reader.ReadAsync(ct)) {
                    var lbId = (ushort)reader.GetInt32(0);
                    if (!temp.TryGetValue(lbId, out var list)) {
                        list = new List<StaticObject>();
                        temp[lbId] = list;
                    }
                    list.Add(new StaticObject {
                        InstanceId = ObjectId.Parse(reader.GetString(1)),
                        ModelId = (uint)reader.GetInt32(2),
                        LayerId = reader.GetString(3),
                        Position = new Vector3(reader.GetFloat(4), reader.GetFloat(5), reader.GetFloat(6)),
                        Rotation = new Quaternion(reader.GetFloat(8), reader.GetFloat(9), reader.GetFloat(10), reader.GetFloat(7)),
                        IsDeleted = reader.GetBoolean(11),
                        CellId = reader.IsDBNull(12) ? null : (uint?)reader.GetInt64(12)
                    });
                }

                foreach (var kvp in temp) results[kvp.Key] = kvp.Value;
                return (IReadOnlyDictionary<ushort, IReadOnlyList<StaticObject>>)results;
            }, ct);
        }

        public async Task<IReadOnlyList<ushort>> GetAffectedLandblocksByLayerAsync(uint regionId, string layerId, ITransaction? tx, CancellationToken ct) {
            return await ExecuteAsync(tx, async (connection, sqliteTx) => {
                var affected = new HashSet<ushort>();

                string[] tables = ["StaticObjects", "Buildings", "EnvCells"];
                foreach (var table in tables) {
                    var sql = $"SELECT DISTINCT LandblockId FROM {table} WHERE LayerId = @layerId AND RegionId = @regionId";
                    using var cmd = new SqliteCommand(sql, connection, sqliteTx);
                    cmd.Parameters.AddWithValue("@layerId", layerId);
                    cmd.Parameters.AddWithValue("@regionId", regionId);
                    using var reader = await cmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct)) affected.Add((ushort)reader.GetInt32(0));
                }
                return (IReadOnlyList<ushort>)affected.ToList();
            }, ct);
        }

        public async Task<IReadOnlyDictionary<ushort, IReadOnlyList<uint>>> GetEnvCellIdsForLandblocksAsync(IEnumerable<ushort> landblockIds, ITransaction? tx, CancellationToken ct) {
            return await ExecuteAsync(tx, async (connection, sqliteTx) => {
                var ids = landblockIds.ToList();
                if (ids.Count == 0) return (IReadOnlyDictionary<ushort, IReadOnlyList<uint>>)new Dictionary<ushort, IReadOnlyList<uint>>();

                var results = new Dictionary<ushort, List<uint>>();
                var idString = string.Join(",", ids.Select(id => id.ToString()));
                var sql = $"SELECT LandblockId, CellId FROM EnvCells WHERE LandblockId IN ({idString})";

                using var cmd = new SqliteCommand(sql, connection, sqliteTx);
                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) {
                    var lbId = (ushort)reader.GetInt32(0);
                    if (!results.TryGetValue(lbId, out var list)) {
                        list = new List<uint>();
                        results[lbId] = list;
                    }
                    list.Add((uint)reader.GetInt64(1));
                }
                return (IReadOnlyDictionary<ushort, IReadOnlyList<uint>>)results.ToDictionary(k => k.Key, v => (IReadOnlyList<uint>)v.Value);
            }, ct);
        }

        public async Task<IReadOnlyList<BuildingObject>> GetBuildingsAsync(ushort? landblockId, ITransaction? tx, CancellationToken ct) {
            return await ExecuteAsync(tx, async (connection, sqliteTx) => {
                var sql = "SELECT InstanceId, ModelId, LayerId, PosX, PosY, PosZ, RotW, RotX, RotY, RotZ, IsDeleted FROM Buildings WHERE 1=1";
                if (landblockId.HasValue) sql += " AND LandblockId = @lbId";

                var results = new List<BuildingObject>();
                using var cmd = new SqliteCommand(sql, connection, sqliteTx);
                if (landblockId.HasValue) cmd.Parameters.AddWithValue("@lbId", (int)landblockId.Value);

                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) {
                    results.Add(new BuildingObject {
                        InstanceId = ObjectId.Parse(reader.GetString(0)),
                        ModelId = (uint)reader.GetInt32(1),
                        LayerId = reader.GetString(2),
                        Position = new Vector3(reader.GetFloat(3), reader.GetFloat(4), reader.GetFloat(5)),
                        Rotation = new Quaternion(reader.GetFloat(7), reader.GetFloat(8), reader.GetFloat(9), reader.GetFloat(6)),
                        IsDeleted = reader.GetBoolean(10)
                    });
                }
                return (IReadOnlyList<BuildingObject>)results;
            }, ct);
        }

        public async Task<IReadOnlyDictionary<ushort, IReadOnlyList<BuildingObject>>> GetBuildingsForLandblocksAsync(IEnumerable<ushort> landblockIds, ITransaction? tx, CancellationToken ct) {
            return await ExecuteAsync(tx, async (connection, sqliteTx) => {
                var ids = landblockIds.ToList();
                if (ids.Count == 0) return (IReadOnlyDictionary<ushort, IReadOnlyList<BuildingObject>>)new Dictionary<ushort, IReadOnlyList<BuildingObject>>();

                var results = ids.ToDictionary(id => id, _ => (IReadOnlyList<BuildingObject>)new List<BuildingObject>());
                var idString = string.Join(",", ids.Select(id => id.ToString()));
                var sql = $"SELECT LandblockId, InstanceId, ModelId, LayerId, PosX, PosY, PosZ, RotW, RotX, RotY, RotZ, IsDeleted FROM Buildings WHERE LandblockId IN ({idString})";

                using var cmd = new SqliteCommand(sql, connection, sqliteTx);
                using var reader = await cmd.ExecuteReaderAsync(ct);
                var temp = new Dictionary<ushort, List<BuildingObject>>();
                while (await reader.ReadAsync(ct)) {
                    var lbId = (ushort)reader.GetInt32(0);
                    if (!temp.TryGetValue(lbId, out var list)) {
                        list = new List<BuildingObject>();
                        temp[lbId] = list;
                    }
                    list.Add(new BuildingObject {
                        InstanceId = ObjectId.Parse(reader.GetString(1)),
                        ModelId = (uint)reader.GetInt32(2),
                        LayerId = reader.GetString(3),
                        Position = new Vector3(reader.GetFloat(4), reader.GetFloat(5), reader.GetFloat(6)),
                        Rotation = new Quaternion(reader.GetFloat(8), reader.GetFloat(9), reader.GetFloat(10), reader.GetFloat(7)),
                        IsDeleted = reader.GetBoolean(11)
                    });
                }
                foreach (var kvp in temp) results[kvp.Key] = kvp.Value;
                return (IReadOnlyDictionary<ushort, IReadOnlyList<BuildingObject>>)results;
            }, ct);
        }

        public async Task<Result<Unit>> UpsertStaticObjectAsync(StaticObject obj, uint regionId, ushort? landblockId, uint? cellId, ITransaction? tx, CancellationToken ct) {
            return await ExecuteAsync(tx, async (connection, sqliteTx) => {
                var sql = @"INSERT INTO StaticObjects (InstanceId, RegionId, LandblockId, CellId, ModelId, LayerId, PosX, PosY, PosZ, RotW, RotX, RotY, RotZ, IsDeleted)
                            VALUES (@id, @regionId, @lbId, @cellId, @modelId, @layerId, @posX, @posY, @posZ, @rotW, @rotX, @rotY, @rotZ, @isDeleted)
                            ON CONFLICT(InstanceId, LayerId) DO UPDATE SET LandblockId = @lbId, CellId = @cellId, ModelId = @modelId, 
                            PosX = @posX, PosY = @posY, PosZ = @posZ, RotW = @rotW, RotX = @rotX, RotY = @rotY, RotZ = @rotZ, IsDeleted = @isDeleted";
                using var cmd = new SqliteCommand(sql, connection, sqliteTx);
                cmd.Parameters.AddWithValue("@id", obj.InstanceId.ToString());
                cmd.Parameters.AddWithValue("@regionId", regionId);
                cmd.Parameters.AddWithValue("@lbId", (object?)landblockId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@cellId", (object?)cellId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@modelId", (int)obj.ModelId);
                cmd.Parameters.AddWithValue("@layerId", obj.LayerId);
                cmd.Parameters.AddWithValue("@posX", obj.Position.X);
                cmd.Parameters.AddWithValue("@posY", obj.Position.Y);
                cmd.Parameters.AddWithValue("@posZ", obj.Position.Z);
                cmd.Parameters.AddWithValue("@rotW", obj.Rotation.W);
                cmd.Parameters.AddWithValue("@rotX", obj.Rotation.X);
                cmd.Parameters.AddWithValue("@rotY", obj.Rotation.Y);
                cmd.Parameters.AddWithValue("@rotZ", obj.Rotation.Z);
                cmd.Parameters.AddWithValue("@isDeleted", obj.IsDeleted);
                await cmd.ExecuteNonQueryAsync(ct);
                return Result<Unit>.Success(Unit.Value);
            }, ct);
        }

        public async Task<Result<Unit>> UpsertBuildingAsync(BuildingObject obj, uint regionId, ushort? landblockId, ITransaction? tx, CancellationToken ct) {
            return await ExecuteAsync(tx, async (connection, sqliteTx) => {
                var sql = @"INSERT INTO Buildings (InstanceId, RegionId, LandblockId, ModelId, LayerId, PosX, PosY, PosZ, RotW, RotX, RotY, RotZ, IsDeleted)
                            VALUES (@id, @regionId, @lbId, @modelId, @layerId, @posX, @posY, @posZ, @rotW, @rotX, @rotY, @rotZ, @isDeleted)
                            ON CONFLICT(InstanceId, LayerId) DO UPDATE SET LandblockId = @lbId, ModelId = @modelId, 
                            PosX = @posX, PosY = @posY, PosZ = @posZ, RotW = @rotW, RotX = @rotX, RotY = @rotY, RotZ = @rotZ, IsDeleted = @isDeleted";
                using var cmd = new SqliteCommand(sql, connection, sqliteTx);
                cmd.Parameters.AddWithValue("@id", obj.InstanceId.ToString());
                cmd.Parameters.AddWithValue("@regionId", regionId);
                cmd.Parameters.AddWithValue("@lbId", (object?)landblockId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@modelId", (int)obj.ModelId);
                cmd.Parameters.AddWithValue("@layerId", obj.LayerId);
                cmd.Parameters.AddWithValue("@posX", obj.Position.X);
                cmd.Parameters.AddWithValue("@posY", obj.Position.Y);
                cmd.Parameters.AddWithValue("@posZ", obj.Position.Z);
                cmd.Parameters.AddWithValue("@rotW", obj.Rotation.W);
                cmd.Parameters.AddWithValue("@rotX", obj.Rotation.X);
                cmd.Parameters.AddWithValue("@rotY", obj.Rotation.Y);
                cmd.Parameters.AddWithValue("@rotZ", obj.Rotation.Z);
                cmd.Parameters.AddWithValue("@isDeleted", obj.IsDeleted);
                await cmd.ExecuteNonQueryAsync(ct);
                return Result<Unit>.Success(Unit.Value);
            }, ct);
        }

        public async Task<Result<Unit>> DeleteBuildingAsync(ObjectId instanceId, ITransaction? tx, CancellationToken ct) {
            return await ExecuteAsync(tx, async (connection, sqliteTx) => {
                using var cmd = new SqliteCommand("UPDATE Buildings SET IsDeleted = 1 WHERE InstanceId = @id", connection, sqliteTx);
                cmd.Parameters.AddWithValue("@id", instanceId.ToString());
                await cmd.ExecuteNonQueryAsync(ct);
                return Result<Unit>.Success(Unit.Value);
            }, ct);
        }

        public async Task<Result<Unit>> DeleteStaticObjectAsync(ObjectId instanceId, ITransaction? tx, CancellationToken ct) {
            return await ExecuteAsync(tx, async (connection, sqliteTx) => {
                using var cmd = new SqliteCommand("UPDATE StaticObjects SET IsDeleted = 1 WHERE InstanceId = @id", connection, sqliteTx);
                cmd.Parameters.AddWithValue("@id", instanceId.ToString());
                await cmd.ExecuteNonQueryAsync(ct);
                return Result<Unit>.Success(Unit.Value);
            }, ct);
        }

        public async Task<Result<Cell>> GetEnvCellAsync(uint cellId, ITransaction? tx, CancellationToken ct) {
            return await ExecuteAsync(tx, async (connection, sqliteTx) => {
                var sql = "SELECT LandblockId, EnvironmentId, Flags, CellStructure, PosX, PosY, PosZ, RotW, RotX, RotY, RotZ, LayerId, MinX, MinY, MinZ, MaxX, MaxY, MaxZ FROM EnvCells WHERE CellId = @id";
                using var cmd = new SqliteCommand(sql, connection, sqliteTx);
                cmd.Parameters.AddWithValue("@id", (long)cellId);
                using var reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct)) {
                    return Result<Cell>.Success(new Cell {
                        CellId = cellId,
                        EnvironmentId = (ushort)reader.GetInt32(1),
                        Flags = (uint)reader.GetInt32(2),
                        CellStructure = (ushort)reader.GetInt32(3),
                        Position = new Vector3(reader.GetFloat(4), reader.GetFloat(5), reader.GetFloat(6)),
                        Rotation = new Quaternion(reader.GetFloat(8), reader.GetFloat(9), reader.GetFloat(10), reader.GetFloat(7)),
                        LayerId = reader.GetString(11),
                        MinBounds = new Vector3(reader.GetFloat(12), reader.GetFloat(13), reader.GetFloat(14)),
                        MaxBounds = new Vector3(reader.GetFloat(15), reader.GetFloat(16), reader.GetFloat(17))
                    });
                }
                return Result<Cell>.Failure(Error.NotFound("EnvCell not found"));
            }, ct);
        }

        public async Task<Result<Unit>> UpsertEnvCellAsync(uint cellId, uint regionId, Cell cell, ITransaction? tx, CancellationToken ct) {
            return await ExecuteAsync(tx, async (connection, sqliteTx) => {
                var lbId = (ushort)(cellId >> 16);
                var sql = @"INSERT INTO EnvCells (CellId, RegionId, LandblockId, EnvironmentId, Flags, CellStructure, PosX, PosY, PosZ, RotW, RotX, RotY, RotZ, LayerId, MinX, MinY, MinZ, MaxX, MaxY, MaxZ)
                            VALUES (@id, @regionId, @lbId, @envId, @flags, @struct, @posX, @posY, @posZ, @rotW, @rotX, @rotY, @rotZ, @layerId, @minX, @minY, @minZ, @maxX, @maxY, @maxZ)
                            ON CONFLICT(CellId) DO UPDATE SET LandblockId = @lbId, EnvironmentId = @envId, Flags = @flags, CellStructure = @struct, PosX = @posX, PosY = @posY, PosZ = @posZ,
                            RotW = @rotW, RotX = @rotX, RotY = @rotY, RotZ = @rotZ, LayerId = @layerId, MinX = @minX, MinY = @minY, MinZ = @minZ, MaxX = @maxX, MaxY = @maxY, MaxZ = @maxZ";
                using var cmd = new SqliteCommand(sql, connection, sqliteTx);
                cmd.Parameters.AddWithValue("@id", (long)cellId);
                cmd.Parameters.AddWithValue("@regionId", regionId);
                cmd.Parameters.AddWithValue("@lbId", lbId);
                cmd.Parameters.AddWithValue("@envId", (int)cell.EnvironmentId);
                cmd.Parameters.AddWithValue("@flags", (long)cell.Flags);
                cmd.Parameters.AddWithValue("@struct", (int)cell.CellStructure);
                cmd.Parameters.AddWithValue("@posX", cell.Position.X);
                cmd.Parameters.AddWithValue("@posY", cell.Position.Y);
                cmd.Parameters.AddWithValue("@posZ", cell.Position.Z);
                cmd.Parameters.AddWithValue("@rotW", cell.Rotation.W);
                cmd.Parameters.AddWithValue("@rotX", cell.Rotation.X);
                cmd.Parameters.AddWithValue("@rotY", cell.Rotation.Y);
                cmd.Parameters.AddWithValue("@rotZ", cell.Rotation.Z);
                cmd.Parameters.AddWithValue("@layerId", cell.LayerId);
                cmd.Parameters.AddWithValue("@minX", cell.MinBounds.X);
                cmd.Parameters.AddWithValue("@minY", cell.MinBounds.Y);
                cmd.Parameters.AddWithValue("@minZ", cell.MinBounds.Z);
                cmd.Parameters.AddWithValue("@maxX", cell.MaxBounds.X);
                cmd.Parameters.AddWithValue("@maxY", cell.MaxBounds.Y);
                cmd.Parameters.AddWithValue("@maxZ", cell.MaxBounds.Z);
                await cmd.ExecuteNonQueryAsync(ct);
                return Result<Unit>.Success(Unit.Value);
            }, ct);
        }

        public async Task<IReadOnlyList<string>> GetTerrainPatchIdsAsync(uint regionId, ITransaction? tx, CancellationToken ct) {
            return await ExecuteAsync(tx, async (connection, sqliteTx) => {
                using var cmd = new SqliteCommand("SELECT Id FROM TerrainPatches WHERE RegionId = @regionId", connection, sqliteTx);
                cmd.Parameters.AddWithValue("@regionId", regionId);
                var results = new List<string>();
                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) results.Add(reader.GetString(0));
                return (IReadOnlyList<string>)results;
            }, ct);
        }

        public async Task<Result<byte[]>> GetTerrainPatchBlobAsync(string id, ITransaction? tx, CancellationToken ct) {
            return await ExecuteAsync(tx, async (connection, sqliteTx) => {
                using var cmd = new SqliteCommand("SELECT Data FROM TerrainPatches WHERE Id = @id", connection, sqliteTx);
                cmd.Parameters.AddWithValue("@id", id);
                var result = await cmd.ExecuteScalarAsync(ct);
                return (result == null || result == DBNull.Value) ? Result<byte[]>.Failure(Error.NotFound("Patch not found")) : Result<byte[]>.Success((byte[])result);
            }, ct);
        }

        public async Task<Result<Unit>> InsertEventAsync(BaseCommand evt, ITransaction? tx, CancellationToken ct) {
            return await ExecuteAsync(tx, async (connection, sqliteTx) => {
                var sql = "INSERT INTO Events (Id, Type, UserId, Created, Data) VALUES (@id, @type, @userId, @created, @data)";
                using var cmd = new SqliteCommand(sql, connection, sqliteTx);
                cmd.Parameters.AddWithValue("@id", evt.Id);
                cmd.Parameters.AddWithValue("@type", evt.GetType().Name);
                cmd.Parameters.AddWithValue("@userId", evt.UserId);
                cmd.Parameters.AddWithValue("@created", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                cmd.Parameters.AddWithValue("@data", evt.Serialize());
                await cmd.ExecuteNonQueryAsync(ct);
                return Result<Unit>.Success(Unit.Value);
            }, ct);
        }

        public async Task<IReadOnlyList<TerrainPatch>> GetTerrainPatchesAsync(uint regionId, ITransaction? tx, CancellationToken ct) {
            return await ExecuteAsync(tx, async (connection, sqliteTx) => {
                using var cmd = new SqliteCommand("SELECT Id, Data, Version FROM TerrainPatches WHERE RegionId = @regionId", connection, sqliteTx);
                cmd.Parameters.AddWithValue("@regionId", regionId);
                var results = new List<TerrainPatch>();
                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) results.Add(new TerrainPatch { Id = reader.GetString(0), Data = (byte[])reader.GetValue(1), Version = (ulong)reader.GetInt64(2) });
                return (IReadOnlyList<TerrainPatch>)results;
            }, ct);
        }

        public async Task<Result<Unit>> UpsertTerrainPatchAsync(string id, uint regionId, byte[] data, ulong version, ITransaction? tx, CancellationToken ct) {
            return await ExecuteAsync(tx, async (connection, sqliteTx) => {
                var sql = "INSERT INTO TerrainPatches (Id, RegionId, Data, Version) VALUES (@id, @regionId, @data, @version) ON CONFLICT(Id) DO UPDATE SET Data = @data, Version = @version, LastModified = CURRENT_TIMESTAMP";
                using var cmd = new SqliteCommand(sql, connection, sqliteTx);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@regionId", regionId);
                cmd.Parameters.AddWithValue("@data", data);
                cmd.Parameters.AddWithValue("@version", (long)version);
                await cmd.ExecuteNonQueryAsync(ct);
                return Result<Unit>.Success(Unit.Value);
            }, ct);
        }

        public async Task<IReadOnlyList<BaseCommand>> GetUnsyncedEventsAsync(ITransaction? tx, CancellationToken ct) {
            return await ExecuteAsync(tx, async (connection, sqliteTx) => {
                using var cmd = new SqliteCommand("SELECT Type, Data FROM Events WHERE ServerTimestamp IS NULL ORDER BY Created ASC", connection, sqliteTx);
                var results = new List<BaseCommand>();
                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) {
                    var evt = BaseCommand.Deserialize((byte[])reader.GetValue(1));
                    if (evt != null) results.Add(evt);
                }
                return (IReadOnlyList<BaseCommand>)results;
            }, ct);
        }

        public async Task<Result<Unit>> UpdateEventServerTimestampAsync(string eventId, ulong serverTimestamp, ITransaction? tx, CancellationToken ct) {
            return await ExecuteAsync(tx, async (connection, sqliteTx) => {
                using var cmd = new SqliteCommand("UPDATE Events SET ServerTimestamp = @ts WHERE Id = @id", connection, sqliteTx);
                cmd.Parameters.AddWithValue("@ts", (long)serverTimestamp);
                cmd.Parameters.AddWithValue("@id", eventId);
                await cmd.ExecuteNonQueryAsync(ct);
                return Result<Unit>.Success(Unit.Value);
            }, ct);
        }

        public async Task<Result<string?>> GetKeyValueAsync(string key, ITransaction? tx, CancellationToken ct) {
            return await ExecuteAsync(tx, async (connection, sqliteTx) => {
                using var cmd = new SqliteCommand("SELECT Value FROM KeyValues WHERE Key = @key", connection, sqliteTx);
                cmd.Parameters.AddWithValue("@key", key);
                var result = await cmd.ExecuteScalarAsync(ct);
                if (result == null || result == DBNull.Value) return Result<string?>.Success(null);
                return Result<string?>.Success((string)result);
            }, ct);
        }

        public async Task<Result<Unit>> SetKeyValueAsync(string key, string? value, ITransaction? tx, CancellationToken ct) {
            return await ExecuteAsync(tx, async (connection, sqliteTx) => {
                var sql = "INSERT INTO KeyValues (Key, Value) VALUES (@key, @value) ON CONFLICT(Key) DO UPDATE SET Value = @value";
                using var cmd = new SqliteCommand(sql, connection, sqliteTx);
                cmd.Parameters.AddWithValue("@key", key);
                cmd.Parameters.AddWithValue("@value", (object?)value ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct);
                return Result<Unit>.Success(Unit.Value);
            }, ct);
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
        }
        
        public ValueTask DisposeAsync() {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
