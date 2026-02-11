using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Repositories;
using Xunit;

namespace WorldBuilder.Shared.Tests.Repositories {
    public class SQLiteProjectRepositoryTests : IAsyncLifetime {
        private readonly TestDatabase _db;
        private readonly SQLiteProjectRepository _repo;

        public SQLiteProjectRepositoryTests() {
            _db = new TestDatabase();
            _repo = new SQLiteProjectRepository(_db.ConnectionString, new NullLogger<SQLiteProjectRepository>());
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

            Assert.Contains("Documents", tables);
            Assert.Contains("Events", tables);

            var indexes = await GetIndexNamesAsync();
            Assert.Contains("idx_events_userid", indexes);
        }

        [Fact]
        public async Task InitializeDatabaseAsync_IsIdempotent() {
            await _repo.InitializeDatabaseAsync(default);
            await _repo.InitializeDatabaseAsync(default); // no error
            var tables = await GetTableNamesAsync();
            Assert.Contains("Documents", tables);
        }

        [Fact]
        public async Task UpsertDocumentAsync_UpdatesLastModified_OnConflict() {
            var docId = new LandscapeDocument().Id;
            var initial = DateTime.UtcNow.AddSeconds(-10);

            // Insert initial
            await using (var cmd = _repo.Connection.CreateCommand()) {
                cmd.CommandText = @"
            INSERT INTO Documents (Id, Type, Data, Version, LastModified)
            VALUES (@id, @type, @data, @ver, @ts)";
                cmd.Parameters.AddWithValue("@id", docId);
                cmd.Parameters.AddWithValue("@type", "TerrainDocument");
                cmd.Parameters.AddWithValue("@data", new byte[] { 1 });
                cmd.Parameters.AddWithValue("@ver", 1L);
                cmd.Parameters.AddWithValue("@ts", initial);
                await cmd.ExecuteNonQueryAsync();
            }

            await Task.Delay(100);

            // Upsert update
            var tx = await _repo.CreateTransactionAsync(default);
            await _repo.UpdateDocumentAsync(docId, new byte[] { 2 }, 2, tx, default);
            await tx.CommitAsync(default);

            // Verify LastModified updated
            await using var readCmd = _repo.Connection.CreateCommand();
            readCmd.CommandText = "SELECT LastModified FROM Documents WHERE Id = @id";
            readCmd.Parameters.AddWithValue("@id", docId);
            var lastModObj = await readCmd.ExecuteScalarAsync();
            var lastMod = DateTime.Parse(lastModObj?.ToString() ?? throw new InvalidOperationException("LastModified not found"));

            Assert.True(lastMod > initial);
        }

        [Fact]
        public async Task UpdateDocumentAsync_Fails_WhenVersionIsLower() {
            var docId = "TestDoc_1";
            var data1 = new byte[] { 1 };
            var data2 = new byte[] { 2 };

            // Insert initial version 10
            await _repo.InsertDocumentAsync(docId, "TestType", data1, 10, null, default);

            // Try to update with version 5
            var result = await _repo.UpdateDocumentAsync(docId, data2, 5, null, default);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Equal("DOCUMENT_NOT_FOUND", result.Error.Code);

            // Verify data is still version 10
            var blobResult = await _repo.GetDocumentBlobAsync<LandscapeDocument>(docId, default);
            Assert.Equal(data1, blobResult.Value);
        }

        private async Task<List<string>> GetTableNamesAsync() {
            var cmd = _repo.Connection.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
            await using var reader = await cmd.ExecuteReaderAsync();
            var tables = new List<string>();
            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));
            return tables;
        }

        private async Task<List<string>> GetIndexNamesAsync() {
            var cmd = _repo.Connection.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%';";
            await using var reader = await cmd.ExecuteReaderAsync();
            var indexes = new List<string>();
            while (await reader.ReadAsync())
                indexes.Add(reader.GetString(0));
            return indexes;
        }
    }
}