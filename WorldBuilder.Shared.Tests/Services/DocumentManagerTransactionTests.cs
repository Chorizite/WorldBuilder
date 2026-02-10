using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Repositories;
using WorldBuilder.Shared.Services;
using Xunit;

namespace WorldBuilder.Shared.Tests.Services {
    public class DocumentManagerTransactionTests : IAsyncLifetime {
        private readonly TestDatabase _db;
        private SQLiteProjectRepository? _repo;
        private Mock<IDatReaderWriter> _datsMock;
        private DocumentManager? _documentManager;

        public DocumentManagerTransactionTests() {
            _db = new TestDatabase();
            _datsMock = new Mock<IDatReaderWriter>();
        }

        public async Task InitializeAsync() {
            _repo = new SQLiteProjectRepository(_db.ConnectionString, new NullLogger<SQLiteProjectRepository>());
            await _repo.InitializeDatabaseAsync(default);
            _documentManager = new DocumentManager(_repo, _datsMock.Object, new NullLogger<DocumentManager>());
            // Don't call InitializeAsync here to avoid the user value lookup that creates transactions before tests
        }

        public Task DisposeAsync() {
            _documentManager?.Dispose();
            _repo?.Dispose();
            _db?.Dispose();

            return Task.CompletedTask;
        }

        [Fact]
        public async Task CreateTransactionAsync_ReturnsITransaction() {
            // Act
            var transaction = await _documentManager!.CreateTransactionAsync(CancellationToken.None);

            // Assert
            Assert.NotNull(transaction);
            Assert.IsAssignableFrom<ITransaction>(transaction);
        }

        [Fact]
        public async Task CreateDocumentAsync_WithTransaction_Succeeds() {
            // Arrange
            var document = new LandscapeDocument();
            var transaction = await _documentManager!.CreateTransactionAsync(CancellationToken.None);

            try {
                // Act
                var rentalResult = await _documentManager.CreateDocumentAsync(document, transaction, CancellationToken.None);

                // Assert
                Assert.True(rentalResult.IsSuccess);
                var rental = rentalResult.Value;
                Assert.NotNull(rental);
                Assert.Equal(document.Id, rental.Document.Id);
            }
            finally {
                await transaction.DisposeAsync();
            }
        }

        [Fact]
        public async Task PersistDocumentAsync_WithTransaction_Succeeds() {
            // Arrange
            var document = new LandscapeDocument();
            var transaction = await _documentManager!.CreateTransactionAsync(CancellationToken.None);

            try {
                // Create and persist the document
                var rentalResult = await _documentManager.CreateDocumentAsync(document, transaction, CancellationToken.None);
                Assert.True(rentalResult.IsSuccess);
                var rental = rentalResult.Value;
                rental.Document.Version = 2; // Modify version to test persistence

                // Act
                var persistResult = await _documentManager.PersistDocumentAsync(rental, transaction, CancellationToken.None);
                Assert.True(persistResult.IsSuccess);
                await transaction.CommitAsync();

                // Verify the document was persisted by getting it outside the transaction
                var newRentalResult = await _documentManager.RentDocumentAsync<LandscapeDocument>(document.Id, CancellationToken.None);
                if (newRentalResult.IsSuccess) {
                    var newRental = newRentalResult.Value;
                    Assert.NotNull(newRental);
                    Assert.Equal(2UL, newRental.Document.Version);
                }
                else {
                    Assert.Fail($"Failed to rent document: {newRentalResult.Error.Message}");
                }
            }
            finally {
                await transaction.DisposeAsync();
            }
        }

        [Fact]
        public async Task TransactionRollback_RestoresPreviousState() {
            // Arrange
            var document = new LandscapeDocument();
            var transaction = await _documentManager!.CreateTransactionAsync(CancellationToken.None);

            try {
                // Create and persist the document within the transaction
                var rentalResult = await _documentManager.CreateDocumentAsync(document, transaction, CancellationToken.None);
                Assert.True(rentalResult.IsSuccess);
                var rental = rentalResult.Value;
                rental.Document.Version = 5; // Modify version

                // Persist to DB within the same transaction
                var persistResult = await _documentManager.PersistDocumentAsync(rental, transaction, CancellationToken.None);
                Assert.True(persistResult.IsSuccess);

                // Before rollback, document should exist in DB (through cache)
                var beforeRollbackResult = await _documentManager.RentDocumentAsync<LandscapeDocument>(document.Id, CancellationToken.None);
                if (beforeRollbackResult.IsSuccess) {
                    var beforeRollback = beforeRollbackResult.Value;
                    Assert.NotNull(beforeRollback);
                    beforeRollback.Dispose(); // Return the rental
                }

                // Act - rollback the transaction
                await transaction.RollbackAsync();

                // Verify: try to load from DB directly by bypassing cache (if possible)
                // For this test, we'll use a fresh DocumentManager instance to avoid cache
                var freshRepo = new SQLiteProjectRepository(_db.ConnectionString, new NullLogger<SQLiteProjectRepository>());
                var freshDats = new Mock<IDatReaderWriter>().Object;
                var freshDocManager = new DocumentManager(freshRepo, freshDats, new NullLogger<DocumentManager>());
                await freshDocManager.InitializeAsync(CancellationToken.None);

                // Document should not exist in database after rollback
                var retrievedRentalResult = await freshDocManager.RentDocumentAsync<LandscapeDocument>(document.Id, CancellationToken.None);
                Assert.True(retrievedRentalResult.IsFailure); // Document should not exist in DB since transaction was rolled back
            }
            finally {
                await transaction.DisposeAsync();
            }
        }

        [Fact]
        public async Task ApplyLocalEventAsync_WithTransaction_Succeeds() {
            // Arrange
            var command = new ReorderLandscapeLayerCommand {
                TerrainDocumentId = "testId",
                LayerId = "layerId",
                NewIndex = 1,
                OldIndex = 0
            }; // Use a real command that returns bool
            var transaction = await _documentManager!.CreateTransactionAsync(CancellationToken.None);

            try {
                // Act - Apply the command and check that it returns a failure result
                var result = await _documentManager.ApplyLocalEventAsync(command, transaction, CancellationToken.None);

                // Assert - Command should fail since document doesn't exist
                Assert.True(result.IsFailure);
            }
            finally {
                await transaction.DisposeAsync();
            }
        }
    }
}