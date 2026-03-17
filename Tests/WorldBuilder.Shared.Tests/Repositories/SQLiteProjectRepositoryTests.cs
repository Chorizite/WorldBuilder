using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Repositories;
using Xunit;

namespace WorldBuilder.Shared.Tests.Repositories {
    public class SQLiteProjectRepositoryTests : IAsyncLifetime {
        private readonly TestDatabase _db;
        private readonly SQLiteProjectRepository _repo;

        public SQLiteProjectRepositoryTests() {
            _db = new TestDatabase();
            _repo = new SQLiteProjectRepository(_db.ConnectionString, NullLoggerFactory.Instance);
        }

        public async Task InitializeAsync() {
            await _repo.InitializeDatabaseAsync(default);
        }

        public Task DisposeAsync() {
            _repo.Dispose();
            _db.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public async Task InitializeDatabaseAsync_CreatesAllTablesAndIndexes() {
            var tables = await GetTableNamesAsync();

            Assert.Contains("TerrainPatches", tables);
            Assert.Contains("Events", tables);

            var indexes = await GetIndexNamesAsync();
            Assert.Contains("idx_events_userid", indexes);
        }

        [Fact]
        public async Task InitializeDatabaseAsync_IsIdempotent() {
            await _repo.InitializeDatabaseAsync(default);
            await _repo.InitializeDatabaseAsync(default); // no error
            var tables = await GetTableNamesAsync();
            Assert.Contains("TerrainPatches", tables);
        }

        [Fact]
        public async Task UpsertTerrainPatchAsync_UpdatesLastModified_OnConflict() {
            var patchId = "TerrainPatch_1_0_0";
            var initial = DateTime.UtcNow.AddSeconds(-10);

            // Insert initial
            using (var connection = new SqliteConnection(_db.ConnectionString)) {
                await connection.OpenAsync();
                await using (var cmd = connection.CreateCommand()) {
                    cmd.CommandText = @"
            INSERT INTO TerrainPatches (Id, RegionId, Data, Version, LastModified)
            VALUES (@id, @regionId, @data, @ver, @ts)";
                    cmd.Parameters.AddWithValue("@id", patchId);
                    cmd.Parameters.AddWithValue("@regionId", 1L);
                    cmd.Parameters.AddWithValue("@data", new byte[] { 1 });
                    cmd.Parameters.AddWithValue("@ver", 1L);
                    cmd.Parameters.AddWithValue("@ts", initial);
                    await cmd.ExecuteNonQueryAsync();
                }

                await Task.Delay(100);

                // Upsert update
                var tx = await _repo.CreateTransactionAsync(default);
                await _repo.UpsertTerrainPatchAsync(patchId, 1, new byte[] { 2 }, 2, tx, default);
                await tx.CommitAsync(default);

                // Verify LastModified updated
                await using var readCmd = connection.CreateCommand();
                readCmd.CommandText = "SELECT LastModified FROM TerrainPatches WHERE Id = @id";
                readCmd.Parameters.AddWithValue("@id", patchId);
                var lastModObj = await readCmd.ExecuteScalarAsync();
                var lastModString = lastModObj?.ToString() ?? throw new InvalidOperationException("LastModified not found");
                var lastMod = DateTime.Parse(lastModString);

                Assert.True(lastMod > initial);
            }
        }

        [Fact]
        public async Task GetTerrainPatchBlobAsync_ReturnsData() {
            var patchId = "TerrainPatch_1_0_0";
            var data = new byte[] { 1, 2, 3 };
            await _repo.UpsertTerrainPatchAsync(patchId, 1, data, 1, null, default);

            var result = await _repo.GetTerrainPatchBlobAsync(patchId, null, default);
            Assert.True(result.IsSuccess);
            Assert.Equal(data, result.Value);
        }

        [Fact]
        public async Task UpsertStaticObjectAsync_Succeeds_OnConflict() {
            var layerId = "Layer1";
            // Create layer first to satisfy FK
            await _repo.UpsertLayerAsync(new LandscapeLayer(layerId, true) { Name = "Base" }, 1, 0, null, default);

            var obj = new StaticObject {
                InstanceId = ObjectId.NewDb(ObjectType.StaticObject),
                ModelId = 1,
                LayerId = layerId,
                Position = new Vector3(1, 2, 3),
                Rotation = Quaternion.Identity
            };

            // Initial insert
            var result1 = await _repo.UpsertStaticObjectAsync(obj, 1, 0x0101, null, null, default);
            Assert.True(result1.IsSuccess);

            // Update same object (should trigger ON CONFLICT)
            obj.Position = new Vector3(4, 5, 6);
            var result2 = await _repo.UpsertStaticObjectAsync(obj, 1, 0x0101, null, null, default);
            Assert.True(result2.IsSuccess);

            // Verify update
            var objects = await _repo.GetStaticObjectsAsync(0x0101, null, null, default);
            Assert.Single(objects);
            Assert.Equal(4, objects[0].Position.X);
        }

        [Fact]
        public async Task UpsertBuildingAsync_Succeeds_OnConflict() {
            var layerId = "Layer1";
            // Create layer first to satisfy FK
            await _repo.UpsertLayerAsync(new LandscapeLayer(layerId, true) { Name = "Base" }, 1, 0, null, default);

            var bldg = new BuildingObject {
                InstanceId = ObjectId.NewDb(ObjectType.Building),
                ModelId = 1,
                LayerId = layerId,
                Position = new Vector3(1, 2, 3),
                Rotation = Quaternion.Identity
            };

            // Initial insert
            var result1 = await _repo.UpsertBuildingAsync(bldg, 1, 0x0101, null, default);
            Assert.True(result1.IsSuccess);

            // Update same building (should trigger ON CONFLICT)
            bldg.Position = new Vector3(4, 5, 6);
            var result2 = await _repo.UpsertBuildingAsync(bldg, 1, 0x0101, null, default);
            Assert.True(result2.IsSuccess);

            // Verify update
            var buildings = await _repo.GetBuildingsAsync(0x0101, null, default);
            Assert.Single(buildings);
            Assert.Equal(4, buildings[0].Position.X);
        }

        private async Task<List<string>> GetTableNamesAsync() {
            using var connection = new SqliteConnection(_db.ConnectionString);
            await connection.OpenAsync();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
            await using var reader = await cmd.ExecuteReaderAsync();
            var tables = new List<string>();
            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));
            return tables;
        }

        private async Task<List<string>> GetIndexNamesAsync() {
            using var connection = new SqliteConnection(_db.ConnectionString);
            await connection.OpenAsync();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%';";
            await using var reader = await cmd.ExecuteReaderAsync();
            var indexes = new List<string>();
            while (await reader.ReadAsync())
                indexes.Add(reader.GetString(0));
            return indexes;
        }
    }
}