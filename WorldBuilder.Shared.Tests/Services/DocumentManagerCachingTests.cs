using Microsoft.Extensions.Logging.Abstractions;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;
using WorldBuilder.Shared.Repositories;
using WorldBuilder.Shared.Tests.Mocks;
using Xunit;

namespace WorldBuilder.Shared.Tests.Services {
    public class DocumentManagerCachingTests : IDisposable {
        private readonly TestDatabase _db;
        private readonly SQLiteProjectRepository _repo;
        private readonly DocumentManager _mgr;

        public DocumentManagerCachingTests() {
            _db = new TestDatabase();
            _repo = new SQLiteProjectRepository(_db.ConnectionString);
            _repo.InitializeDatabaseAsync(default).Wait();
            _mgr = new DocumentManager(_repo, new MockDatReaderWriter(), NullLogger<DocumentManager>.Instance);
        }

        public void Dispose() {
            _mgr.Dispose();
            _repo.Dispose();
            _db.Dispose();
        }

        [Fact]
        public async Task RentDocumentAsync_ReturnsSameInstance_WhenRentalIsHeld() {
            // Arrange
            var docId = "LandscapeDocument_1";
            var doc = new LandscapeDocument(docId);
            await using var tx = await _mgr.CreateTransactionAsync(default);
            await _mgr.CreateDocumentAsync(doc, tx, default);
            await tx.CommitAsync(default);

            // Act
            var rent1Result = await _mgr.RentDocumentAsync<LandscapeDocument>(docId, default);
            Assert.True(rent1Result.IsSuccess);
            using var rental1 = rent1Result.Value;

            var rent2Result = await _mgr.RentDocumentAsync<LandscapeDocument>(docId, default);
            Assert.True(rent2Result.IsSuccess);
            using var rental2 = rent2Result.Value;

            // Assert
            Assert.Same(rental1.Document, rental2.Document);
        }

        [Fact]
        public async Task RentDocumentAsync_ReturnsSameInstance_AfterReturn_IfStillInCache() {
            // Arrange
            var docId = "LandscapeDocument_1";
            var doc = new LandscapeDocument(docId);
            await using var tx = await _mgr.CreateTransactionAsync(default);
            await _mgr.CreateDocumentAsync(doc, tx, default);
            await tx.CommitAsync(default);

            // Act
            BaseDocument? instance1;
            {
                var rentResult = await _mgr.RentDocumentAsync<LandscapeDocument>(docId, default);
                using var rental = rentResult.Value;
                instance1 = rental.Document;
            } // Rental disposed, rent count becomes 0

            var rentResult2 = await _mgr.RentDocumentAsync<LandscapeDocument>(docId, default);
            using var rental2 = rentResult2.Value;
            var instance2 = rental2.Document;

            // Assert
            // Since we just returned it, it should still be in the strong reference in the cache 
            // WAIT! My recent change to DocumentManager:
            // "Release strong reference when no longer rented"
            // So it should be a weak reference now.
            // If GC hasn't run, it should be the same instance.
            Assert.Same(instance1, instance2);
        }
        
        [Fact]
        public async Task RentDocumentAsync_ReturnsNewInstance_AfterGC_IfRentCountIsZero() {
             // Arrange
            var docId = "LandscapeDocument_1";
            var doc = new LandscapeDocument(docId);
            await using var tx = await _mgr.CreateTransactionAsync(default);
            await _mgr.CreateDocumentAsync(doc, tx, default);
            await tx.CommitAsync(default);

            // Act
            WeakReference weakRef;
            {
                var rentResult = await _mgr.RentDocumentAsync<LandscapeDocument>(docId, default);
                using var rental = rentResult.Value;
                weakRef = new WeakReference(rental.Document);
            } // RentCount = 0, _strongRef = null

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.False(weakRef.IsAlive);

            var rentResult2 = await _mgr.RentDocumentAsync<LandscapeDocument>(docId, default);
            using var rental2 = rentResult2.Value;
            
            // Assert
            Assert.NotNull(rental2.Document);
            // Instance should be different as it was re-loaded from DB
            // (Assuming weakRef.Target is null)
        }
    }
}
