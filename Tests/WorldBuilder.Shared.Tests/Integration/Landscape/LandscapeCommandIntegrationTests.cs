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
    public class LandscapeCommandIntegrationTests : IDisposable {
        private readonly TestDatabase _db;
        private readonly SQLiteProjectRepository _repo;
        private readonly MockDatReaderWriter _dats;
        private readonly DocumentManager _docManager;
        private readonly uint _regionId = 1;

        public LandscapeCommandIntegrationTests() {
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
        public async Task CreateDocument_CreatesDocumentWithBaseLayer_AndCanRent() {
            // Arrange
            var command = new CreateLandscapeDocumentCommand(_regionId);
            await using var tx = await _docManager.CreateTransactionAsync(default);

            // Act
            var result = await _docManager.ApplyLocalEventAsync(command, tx, default);
            await tx.CommitAsync(default);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);

            var docId = LandscapeDocument.GetIdFromRegion(_regionId);
            var rentResult = await _docManager.RentDocumentAsync<LandscapeDocument>(docId, default);
            Assert.True(rentResult.IsSuccess);
            using var rental = rentResult.Value;

            Assert.Single(rental.Document.GetAllLayers());
            Assert.True(rental.Document.GetAllLayers().First().IsBase);
        }

        [Fact]
        public async Task LayerLifecycle_CompleteWorkflow_Succeeds() {
            // 1. Create Document
            var docId = LandscapeDocument.GetIdFromRegion(_regionId);
            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                await _docManager.ApplyLocalEventAsync(new CreateLandscapeDocumentCommand(_regionId), tx, default);
                await tx.CommitAsync(default);
            }

            // 2. Create Layer
            string layerId;
            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                var cmd = new CreateLandscapeLayerCommand(docId, [], "New Layer", false);
                var res = await _docManager.ApplyLocalEventAsync(cmd, tx, default);
                await tx.CommitAsync(default);
                Assert.True(res.IsSuccess);
                layerId = res.Value!;
            }

            // 3. Edit Terrain (Simulation)
            {
                var rentResult = await _docManager.RentDocumentAsync<LandscapeDocument>(docId, default);
                using var rental = rentResult.Value;
                Assert.Contains(rental.Document.GetAllLayers(), l => l.Id == layerId);
            }

            // 4. Delete Layer
            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                var cmd = new DeleteLandscapeLayerCommand {
                    TerrainDocumentId = docId,
                    GroupPath = [],
                    LayerId = layerId
                };
                var res = await _docManager.ApplyLocalEventAsync(cmd, tx, default);
                await tx.CommitAsync(default);
                Assert.True(res.IsSuccess);
            }

            // 5. Verify Deleted
            {
                var rentResult = await _docManager.RentDocumentAsync<LandscapeDocument>(docId, default);
                using var rental = rentResult.Value;
                Assert.DoesNotContain(rental.Document.GetAllLayers(), l => l.Id == layerId);
            }
        }

        [Fact]
        public async Task CreateMultipleLayers_ThenReorder_MaintainsConsistency() {
            var docId = LandscapeDocument.GetIdFromRegion(_regionId);
            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                await _docManager.ApplyLocalEventAsync(new CreateLandscapeDocumentCommand(_regionId), tx, default);
                await tx.CommitAsync(default);
            }

            string layer1Id, layer2Id;
            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                layer1Id = (await _docManager.ApplyLocalEventAsync(
                    new CreateLandscapeLayerCommand(docId, [], "L1", false), tx, default)).Value!;
                layer2Id = (await _docManager.ApplyLocalEventAsync(
                    new CreateLandscapeLayerCommand(docId, [], "L2", false), tx, default)).Value!;
                await tx.CommitAsync(default);
            }

            // Reorder: Move L2 to index 1 (after base layer at 0)
            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                var cmd = new ReorderLandscapeLayerCommand {
                    TerrainDocumentId = docId,
                    GroupPath = [],
                    LayerId = layer2Id,
                    NewIndex = 1,
                    OldIndex = 2
                };
                await _docManager.ApplyLocalEventAsync(cmd, tx, default);
                await tx.CommitAsync(default);
            }

            // Verify Order: Base (0), L2 (1), L1 (2)
            {
                var rentResult = await _docManager.RentDocumentAsync<LandscapeDocument>(docId, default);
                using var rental = rentResult.Value;
                var layers = rental.Document.GetAllLayers().ToList();
                Assert.Equal(3, layers.Count);
                Assert.True(layers[0].IsBase);
                Assert.Equal(layer2Id, layers[1].Id);
                Assert.Equal(layer1Id, layers[2].Id);
            }
        }

        [Fact]
        public async Task CreateLayersInNestedGroups_NavigationWorks() {
            var docId = LandscapeDocument.GetIdFromRegion(_regionId);
            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                await _docManager.ApplyLocalEventAsync(new CreateLandscapeDocumentCommand(_regionId), tx, default);
                await tx.CommitAsync(default);
            }

            // Create Group A
            string groupAId = Guid.NewGuid().ToString();
            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                var cmd = new CreateLandscapeLayerGroupCommand(docId, [], "Group A") { GroupId = groupAId };
                await _docManager.ApplyLocalEventAsync(cmd, tx, default);
                await tx.CommitAsync(default);
            }

            // Create Group B inside Group A
            string groupBId = Guid.NewGuid().ToString();
            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                var cmd = new CreateLandscapeLayerGroupCommand(docId, [groupAId], "Group B") { GroupId = groupBId };
                await _docManager.ApplyLocalEventAsync(cmd, tx, default);
                await tx.CommitAsync(default);
            }

            // Create Layer inside Group B
            string layerId;
            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                var cmd = new CreateLandscapeLayerCommand(docId, [groupAId, groupBId], "Nested Layer", false);
                var res = await _docManager.ApplyLocalEventAsync(cmd, tx, default);
                await tx.CommitAsync(default);
                layerId = res.Value!;
            }

            // Verify Navigation
            {
                var rentResult = await _docManager.RentDocumentAsync<LandscapeDocument>(docId, default);
                using var rental = rentResult.Value;

                var parentB = rental.Document.FindParentGroup([groupAId, groupBId]);
                Assert.NotNull(parentB);
                Assert.Equal(groupBId, parentB.Id);
                Assert.Contains(parentB.Children, c => c.Id == layerId);
            }
        }

        [Fact]
        public async Task CreateLayer_Undo_Redo_MaintainsState() {
            var docId = LandscapeDocument.GetIdFromRegion(_regionId);
            var undoStack = new UndoStack(_docManager);

            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                await _docManager.ApplyLocalEventAsync(new CreateLandscapeDocumentCommand(_regionId), tx, default);
                await tx.CommitAsync(default);
            }

            // 1. Create Layer
            string layerId;
            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                var cmd = new CreateLandscapeLayerCommand(docId, [], "Undoable Layer", false);
                var res = await _docManager.ApplyLocalEventAsync(cmd, tx, default);
                await tx.CommitAsync(default);
                layerId = res.Value!;
                undoStack.Push([cmd]);
            }

            // Verify exists
            {
                var rentResult = await _docManager.RentDocumentAsync<LandscapeDocument>(docId, default);
                using var rental = rentResult.Value;
                Assert.Contains(rental.Document.GetAllLayers(), l => l.Id == layerId);
            }

            // 2. Undo
            await undoStack.UndoAsync(default);

            // Verify gone
            {
                var rentResult = await _docManager.RentDocumentAsync<LandscapeDocument>(docId, default);
                using var rental = rentResult.Value;
                Assert.DoesNotContain(rental.Document.GetAllLayers(), l => l.Id == layerId);
            }

            // 3. Redo
            await undoStack.RedoAsync(default);

            // Verify back
            {
                var rentResult = await _docManager.RentDocumentAsync<LandscapeDocument>(docId, default);
                using var rental = rentResult.Value;
                Assert.Contains(rental.Document.GetAllLayers(), l => l.Id == layerId);
            }
        }

        [Fact]
        public async Task ReorderLayer_Undo_Redo_RestoresOrder() {
            var docId = LandscapeDocument.GetIdFromRegion(_regionId);
            var undoStack = new UndoStack(_docManager);

            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                await _docManager.ApplyLocalEventAsync(new CreateLandscapeDocumentCommand(_regionId), tx, default);
                await tx.CommitAsync(default);
            }

            string l1Id;
            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                var cmd = new CreateLandscapeLayerCommand(docId, [], "L1", false);
                var res = await _docManager.ApplyLocalEventAsync(cmd, tx, default);
                await tx.CommitAsync(default);
                l1Id = res.Value!;
            }

            string l2Id;
            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                var cmd = new CreateLandscapeLayerCommand(docId, [], "L2", false);
                var res = await _docManager.ApplyLocalEventAsync(cmd, tx, default);
                await tx.CommitAsync(default);
                l2Id = res.Value!;
            }

            // Reorder L2 to index 1
            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                var cmd = new ReorderLandscapeLayerCommand {
                    TerrainDocumentId = docId,
                    GroupPath = [],
                    LayerId = l2Id,
                    NewIndex = 1,
                    OldIndex = 2
                };
                await _docManager.ApplyLocalEventAsync(cmd, tx, default);
                await tx.CommitAsync(default);
                undoStack.Push([cmd]);
            }

            // Verify Order: Base, L2, L1
            {
                var rentResult = await _docManager.RentDocumentAsync<LandscapeDocument>(docId, default);
                using var rental = rentResult.Value;
                var layers = rental.Document.GetAllLayers().ToList();
                Assert.Equal(l2Id, layers[1].Id);
            }

            // Undo
            await undoStack.UndoAsync(default);

            // Verify Order: Base, L1, L2
            {
                var rentResult = await _docManager.RentDocumentAsync<LandscapeDocument>(docId, default);
                using var rental = rentResult.Value;
                var layers = rental.Document.GetAllLayers().ToList();
                Assert.Equal(l1Id, layers[1].Id);
                Assert.Equal(l2Id, layers[2].Id);
            }
        }

        [Fact]
        public async Task DeleteLayer_Undo_RecreatesLayer() {
            var docId = LandscapeDocument.GetIdFromRegion(_regionId);
            var undoStack = new UndoStack(_docManager);

            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                await _docManager.ApplyLocalEventAsync(new CreateLandscapeDocumentCommand(_regionId), tx, default);
                await tx.CommitAsync(default);
            }

            string layerId;
            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                var cmd = new CreateLandscapeLayerCommand(docId, [], "To be deleted", false);
                var res = await _docManager.ApplyLocalEventAsync(cmd, tx, default);
                await tx.CommitAsync(default);
                layerId = res.Value!;
            }

            // Delete
            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                var cmd = new DeleteLandscapeLayerCommand {
                    TerrainDocumentId = docId,
                    GroupPath = [],
                    LayerId = layerId
                };
                await _docManager.ApplyLocalEventAsync(cmd, tx, default);
                await tx.CommitAsync(default);
                undoStack.Push([cmd]);
            }

            // Undo
            await undoStack.UndoAsync(default);

            // Verify recreates
            {
                var rentResult = await _docManager.RentDocumentAsync<LandscapeDocument>(docId, default);
                using var rental = rentResult.Value;
                Assert.Contains(rental.Document.GetAllLayers(), l => l.Id == layerId);
            }
        }

        [Fact]
        public async Task MultipleOperations_Followed_ByMultipleUndos_WorksCorrectly() {
            var docId = LandscapeDocument.GetIdFromRegion(_regionId);
            var undoStack = new UndoStack(_docManager);

            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                await _docManager.ApplyLocalEventAsync(new CreateLandscapeDocumentCommand(_regionId), tx, default);
                await tx.CommitAsync(default);
            }

            // Op 1: Create L1
            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                var cmd = new CreateLandscapeLayerCommand(docId, [], "L1", false);
                await _docManager.ApplyLocalEventAsync(cmd, tx, default);
                await tx.CommitAsync(default);
                undoStack.Push([cmd]);
            }

            // Op 2: Create L2
            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                var cmd = new CreateLandscapeLayerCommand(docId, [], "L2", false);
                await _docManager.ApplyLocalEventAsync(cmd, tx, default);
                await tx.CommitAsync(default);
                undoStack.Push([cmd]);
            }

            // Undo 2
            await undoStack.UndoAsync(default);
            {
                var rentResult = await _docManager.RentDocumentAsync<LandscapeDocument>(docId, default);
                using var rental = rentResult.Value;
                Assert.Single(rental.Document.GetAllLayers(), l => !l.IsBase);
            }

            // Undo 1
            await undoStack.UndoAsync(default);
            {
                var rentResult = await _docManager.RentDocumentAsync<LandscapeDocument>(docId, default);
                using var rental = rentResult.Value;
                Assert.DoesNotContain(rental.Document.GetAllLayers(), l => !l.IsBase);
            }
        }

        [Fact]
        public async Task VersionIncrementsOnEachChange() {
            var docId = LandscapeDocument.GetIdFromRegion(_regionId);
            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                await _docManager.ApplyLocalEventAsync(new CreateLandscapeDocumentCommand(_regionId), tx, default);
                await tx.CommitAsync(default);
            }

            ulong initialVersion;
            {
                var rentResult = await _docManager.RentDocumentAsync<LandscapeDocument>(docId, default);
                using var rental = rentResult.Value;
                initialVersion = rental.Document.Version;
            }

            // Apply a change
            {
                await using var tx = await _docManager.CreateTransactionAsync(default);
                var cmd = new CreateLandscapeLayerCommand(docId, [], "New Layer", false);
                await _docManager.ApplyLocalEventAsync(cmd, tx, default);
                await tx.CommitAsync(default);
            }

            // Verify version incremented
            {
                var rentResult = await _docManager.RentDocumentAsync<LandscapeDocument>(docId, default);
                using var rental = rentResult.Value;
                Assert.True(rental.Document.Version > initialVersion);
            }
        }
    }
}