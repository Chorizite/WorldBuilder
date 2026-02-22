using MemoryPack;
using MemoryPack.Formatters;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Repositories;
using WorldBuilder.Shared.Services;
using WorldBuilder.Shared.Tests.Mocks;

namespace WorldBuilder.Shared.Tests.Services {
    public class DocumentManagerTests : IAsyncLifetime {
        private readonly TestDatabase _db;
        private SqliteConnection Connection => _repo.Connection;
        private readonly SQLiteProjectRepository _repo;
        private readonly DocumentManager _mgr;

        public DocumentManagerTests() {
            _db = new TestDatabase();
            _repo = new SQLiteProjectRepository(_db.ConnectionString);
            _mgr = new DocumentManager(_repo, new MockDatReaderWriter(), new NullLogger<DocumentManager>());
        }

        public async Task InitializeAsync()
            => await _repo.InitializeDatabaseAsync(default);

        public async Task DisposeAsync() {
            _mgr.Dispose();
            await Connection.CloseAsync();
            await Connection.DisposeAsync();
        }

        [Fact]
        public async Task RentDocumentAsync_ReturnsNull_WhenDocumentMissing() {
            var docId = $"MockTerrainDocument_{Guid.NewGuid()}";
            var docResult = await _mgr.RentDocumentAsync<LandscapeDocument>(docId, default);
            Assert.True(docResult.IsFailure);
        }
    }
}