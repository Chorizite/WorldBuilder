using Microsoft.Extensions.Logging.Abstractions;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Repositories;
using WorldBuilder.Shared.Services;
using WorldBuilder.Shared.Tests.Mocks;
using Xunit;

namespace WorldBuilder.Shared.Tests.Integration.Landscape {
    public class LayerPersistenceTests : IDisposable {
        private readonly TestDatabase _db;
        private readonly SQLiteProjectRepository _repo;
        private readonly MockDatReaderWriter _dats;
        private readonly DocumentManager _docManager;
        private readonly uint _regionId = 1;

        public LayerPersistenceTests() {
            _db = new TestDatabase();
            _repo = new SQLiteProjectRepository(_db.ConnectionString);
            _repo.InitializeDatabaseAsync(default).Wait();
            _dats = new MockDatReaderWriter();
            _docManager = new DocumentManager(_repo, _dats, NullLogger<DocumentManager>.Instance);
            _docManager.InitializeAsync(default).Wait();
        }

        public void Dispose() {
            _docManager.Dispose();
            _repo.Dispose();
            _db.Dispose();
        }



        [Fact]
        public async Task AddLayer_ViaCommand_Succeeds() {
            // 1. Create Document
            var docId = LandscapeDocument.GetIdFromRegion(_regionId);
            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                await _docManager.ApplyLocalEventAsync(new CreateLandscapeDocumentCommand(_regionId), tx, default);
                await tx.CommitAsync(default);
            }

            // 2. Add Layer VIA COMMAND
            string layerId;
            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                var cmd = new CreateLandscapeLayerCommand(docId, [], "Proper Layer", false);
                var res = await _docManager.ApplyLocalEventAsync(cmd, tx, default);
                await tx.CommitAsync(default);
                Assert.True(res.IsSuccess);
                layerId = res.Value!;
            }

            // 3. Clear Cache
            _docManager.Dispose();

            var newRepo = new SQLiteProjectRepository(_db.ConnectionString);
            var newDocManager = new DocumentManager(newRepo, _dats, NullLogger<DocumentManager>.Instance);
            await newDocManager.InitializeAsync(default);

            // 4. Try to Load
            {
                var rentResult = await newDocManager.RentDocumentAsync<LandscapeDocument>(docId, default);
                Assert.True(rentResult.IsSuccess);
                using var rental = rentResult.Value;

                // Should init fine
                await rental.Document.InitializeForUpdatingAsync(_dats, newDocManager, default);

                Assert.Contains(rental.Document.GetAllLayers(), l => l.Id == layerId);
            }

            newDocManager.Dispose();
        }
    }
}
