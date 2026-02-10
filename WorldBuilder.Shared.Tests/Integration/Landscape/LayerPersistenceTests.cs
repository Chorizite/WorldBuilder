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
        public async Task AddLayer_ViaModelDirectly_FailsToLoad_WhenLayerDocumentMissing() {
            // 1. Create Document
            var docId = LandscapeDocument.GetIdFromRegion(_regionId);
            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                await _docManager.ApplyLocalEventAsync(new CreateLandscapeDocumentCommand(_regionId), tx, default);
                await tx.CommitAsync(default);
            }

            // 2. Add Layer DIRECTLY (Simulating the bug in LayersPanelViewModel)
            // We do NOT create the LandscapeLayerDocument
            string layerId = LandscapeLayerDocument.CreateId();
            {
                var rentResult = await _docManager.RentDocumentAsync<LandscapeDocument>(docId, default);
                using var rental = rentResult.Value;
                
                // Direct modification like the UI does
                rental.Document.AddLayer([], "Buggy Layer", false, layerId);
                
                // Save the LandscapeDocument
                await using var tx = await _docManager.CreateTransactionAsync(default);
                await _docManager.PersistDocumentAsync(rental, tx, default);
                await tx.CommitAsync(default);
            }

            // 3. Clear Cache (simulate restarting app)
            _docManager.Dispose(); // Dispose old manager to clear cache
            
            // Re-init manager
            var newRepo = new SQLiteProjectRepository(_db.ConnectionString); // Re-use same DB
            var newDocManager = new DocumentManager(newRepo, _dats, NullLogger<DocumentManager>.Instance);
            await newDocManager.InitializeAsync(default);

            // 4. Try to Load
            // This should fail or return a document with broken layers because the LayerDocument is missing
            {
                // We expect this to fail or throw when initializing because the layer document is missing
                var rentResult = await newDocManager.RentDocumentAsync<LandscapeDocument>(docId, default);
                
                // If rent succeeds (it might just load the blob), then initializing should fail
                if (rentResult.IsSuccess) {
                     using var rental = rentResult.Value;
                     // InitializeForUpdatingAsync loads layers, so this is where it should blow up
                     await Assert.ThrowsAsync<InvalidOperationException>(async () => 
                        await rental.Document.InitializeForUpdatingAsync(_dats, newDocManager, default));
                }
            }
            
            newDocManager.Dispose();
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
                layerId = res.Value!.Document.Id;
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
