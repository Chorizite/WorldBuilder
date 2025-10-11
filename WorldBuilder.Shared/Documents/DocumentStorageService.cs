using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Documents {
    /// <summary>
    /// Manages database operations for documents, updates, and snapshots using Entity Framework Core.
    /// </summary>
    public class DocumentStorageService : IDocumentStorageService, IDisposable {
        private readonly DocumentDbContext _context;
        private readonly ILogger<DocumentStorageService> _logger;
        private readonly SemaphoreSlim _contextLock = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public DocumentStorageService(DocumentDbContext context, ILogger<DocumentStorageService> logger) {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Retrieves a document by its ID without tracking.
        /// </summary>
        /// <param name="documentId">The ID of the document to retrieve.</param>
        /// <returns>The document, or null if not found.</returns>
        /// <exception cref="ArgumentNullException">Thrown if documentId is null or empty.</exception>
        public async Task<DBDocument?> GetDocumentAsync(string documentId) {
            ValidateDocumentId(documentId);
            return await ExecuteWithLockAsync(async () => {
                _context.ChangeTracker.Clear();
                return await _context.Documents
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == documentId);
            }, $"Retrieved document {documentId}");
        }

        /// <summary>
        /// Creates a new document with the specified ID, type, and initial data.
        /// </summary>
        /// <param name="documentId">The ID of the document.</param>
        /// <param name="type">The type of the document.</param>
        /// <param name="initialData">The initial data for the document.</param>
        /// <returns>The created document.</returns>
        /// <exception cref="ArgumentNullException">Thrown if documentId, type, or initialData is null or empty.</exception>
        public async Task<DBDocument> CreateDocumentAsync(string documentId, string type, byte[] initialData) {
            ValidateDocumentId(documentId);
            if (string.IsNullOrEmpty(type)) throw new ArgumentNullException(nameof(type));
            if (initialData == null) throw new ArgumentNullException(nameof(initialData));

            var document = new DBDocument {
                Id = documentId,
                Type = type,
                Data = initialData,
                LastModified = DateTime.UtcNow
            };

            return await ExecuteWithLockAsync(async () => {
                _context.ChangeTracker.Clear();
                using var transaction = await _context.Database.BeginTransactionAsync();
                _context.Documents.Add(document);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                _logger.LogInformation("Created document {DocumentId} of type {Type} ({Size} bytes)", documentId, type, initialData.Length);
                return document;
            }, $"Created document {documentId}");
        }

        /// <summary>
        /// Updates an existing document with new data.
        /// </summary>
        /// <param name="documentId">The ID of the document to update.</param>
        /// <param name="update">The updated data.</param>
        /// <returns>The updated document.</returns>
        /// <exception cref="ArgumentNullException">Thrown if documentId or update is null or empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the document is not found.</exception>
        public async Task<DBDocument> UpdateDocumentAsync(string documentId, byte[] update) {
            ValidateDocumentId(documentId);
            if (update == null) throw new ArgumentNullException(nameof(update));

            return await ExecuteWithLockAsync(async () => {
                _context.ChangeTracker.Clear();
                _logger.LogInformation("Starting update for document {DocumentId}", documentId);

                var existingDocument = await _context.Documents
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == documentId);
                _logger.LogDebug("Queried document {DocumentId}, found: {Found}", documentId, existingDocument != null);

                if (existingDocument == null) {
                    _logger.LogError("Document {DocumentId} not found", documentId);
                    throw new InvalidOperationException($"Document {documentId} not found");
                }

                var updatedDocument = new DBDocument {
                    Id = documentId,
                    Data = update,
                    LastModified = DateTime.UtcNow,
                    Type = existingDocument.Type
                };

                using var transaction = await _context.Database.BeginTransactionAsync();
                _context.Documents.Attach(updatedDocument);
                _context.Entry(updatedDocument).State = EntityState.Modified;
                _logger.LogDebug("Attached and marked document {DocumentId} as modified, state: {State}", documentId, _context.Entry(updatedDocument).State);

                var changes = await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                _logger.LogInformation("Saved {Changes} changes and committed transaction for document {DocumentId} ({Size} bytes)", changes, documentId, update.Length);
                return updatedDocument;
            }, $"Updated document {documentId}");
        }

        /// <summary>
        /// Deletes a document by its ID.
        /// </summary>
        /// <param name="documentId">The ID of the document to delete.</param>
        /// <returns>True if the document was deleted, false otherwise.</returns>
        /// <exception cref="ArgumentNullException">Thrown if documentId is null or empty.</exception>
        public async Task<bool> DeleteDocumentAsync(string documentId) {
            ValidateDocumentId(documentId);
            return await ExecuteWithLockAsync(async () => {
                _context.ChangeTracker.Clear();
                using var transaction = await _context.Database.BeginTransactionAsync();
                var rowsAffected = await _context.Documents
                    .Where(d => d.Id == documentId)
                    .ExecuteDeleteAsync();

                if (rowsAffected > 0) {
                    await transaction.CommitAsync();
                    _logger.LogInformation("Deleted document {DocumentId}", documentId);
                }
                return rowsAffected > 0;
            }, $"Deleted document {documentId}");
        }

        /// <summary>
        /// Creates a new document update entry.
        /// </summary>
        /// <param name="documentId">The ID of the document.</param>
        /// <param name="type">The type of the update.</param>
        /// <param name="update">The update data.</param>
        /// <returns>The created document update.</returns>
        /// <exception cref="ArgumentNullException">Thrown if documentId, type, or update is null or empty.</exception>
        public async Task<DBDocumentUpdate> CreateUpdateAsync(string documentId, string type, byte[] update) {
            ValidateDocumentId(documentId);
            if (string.IsNullOrEmpty(type)) throw new ArgumentNullException(nameof(type));
            if (update == null) throw new ArgumentNullException(nameof(update));

            var dbUpdate = new DBDocumentUpdate {
                DocumentId = documentId,
                Type = type,
                Data = update,
                Timestamp = DateTime.UtcNow,
                Id = Guid.NewGuid(),
                ClientId = Guid.NewGuid()
            };

            return await ExecuteWithLockAsync(async () => {
                _context.ChangeTracker.Clear();
                using var transaction = await _context.Database.BeginTransactionAsync();
                _context.Updates.Add(dbUpdate);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                _logger.LogInformation("Created update for document {DocumentId} ({Size} bytes)", documentId, update.Length);
                return dbUpdate;
            }, $"Created update for document {documentId}");
        }

        /// <summary>
        /// Retrieves all updates for a document, ordered by timestamp.
        /// </summary>
        /// <param name="documentId">The ID of the document.</param>
        /// <returns>A list of document updates.</returns>
        /// <exception cref="ArgumentNullException">Thrown if documentId is null or empty.</exception>
        public async Task<List<DBDocumentUpdate>> GetDocumentUpdatesAsync(string documentId) {
            ValidateDocumentId(documentId);
            return await ExecuteWithLockAsync(async () => {
                _context.ChangeTracker.Clear();
                return await _context.Updates
                    .AsNoTracking()
                    .Where(x => x.DocumentId == documentId)
                    .OrderBy(x => x.Timestamp)
                    .ToListAsync();
            }, $"Retrieved updates for document {documentId}");
        }

        /// <summary>
        /// Creates a new snapshot for a document.
        /// </summary>
        /// <param name="snapshot">The snapshot to create.</param>
        /// <returns>The created snapshot.</returns>
        /// <exception cref="ArgumentNullException">Thrown if snapshot or its DocumentId/Data is null or empty.</exception>
        public async Task<DBSnapshot> CreateSnapshotAsync(DBSnapshot snapshot) {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (string.IsNullOrEmpty(snapshot.DocumentId)) throw new ArgumentNullException(nameof(snapshot.DocumentId));
            if (snapshot.Data == null) throw new ArgumentNullException(nameof(snapshot.Data));

            return await ExecuteWithLockAsync(async () => {
                _context.ChangeTracker.Clear();
                using var transaction = await _context.Database.BeginTransactionAsync();
                _context.Snapshots.Add(snapshot);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                _logger.LogInformation("Created snapshot {SnapshotId} for document {DocumentId} ({Size} bytes)", snapshot.Id, snapshot.DocumentId, snapshot.Data.Length);
                return snapshot;
            }, $"Created snapshot for document {snapshot.DocumentId}");
        }

        /// <summary>
        /// Retrieves a snapshot by its ID.
        /// </summary>
        /// <param name="snapshotId">The ID of the snapshot.</param>
        /// <returns>The snapshot, or null if not found.</returns>
        public async Task<DBSnapshot?> GetSnapshotAsync(Guid snapshotId) {
            return await ExecuteWithLockAsync(async () => {
                _context.ChangeTracker.Clear();
                return await _context.Snapshots
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == snapshotId);
            }, $"Retrieved snapshot {snapshotId}");
        }

        /// <summary>
        /// Retrieves all snapshots for a document, ordered by timestamp.
        /// </summary>
        /// <param name="documentId">The ID of the document.</param>
        /// <returns>A list of snapshots.</returns>
        /// <exception cref="ArgumentNullException">Thrown if documentId is null or empty.</exception>
        public async Task<List<DBSnapshot>> GetSnapshotsAsync(string documentId) {
            ValidateDocumentId(documentId);
            return await ExecuteWithLockAsync(async () => {
                _context.ChangeTracker.Clear();
                return await _context.Snapshots
                    .AsNoTracking()
                    .Where(s => s.DocumentId == documentId)
                    .OrderBy(s => s.Timestamp)
                    .ToListAsync();
            }, $"Retrieved snapshots for document {documentId}");
        }

        /// <summary>
        /// Deletes a snapshot by its ID.
        /// </summary>
        /// <param name="snapshotId">The ID of the snapshot to delete.</param>
        /// <returns>True if the snapshot was deleted, false otherwise.</returns>
        public async Task<bool> DeleteSnapshotAsync(Guid snapshotId) {
            return await ExecuteWithLockAsync(async () => {
                _context.ChangeTracker.Clear();
                using var transaction = await _context.Database.BeginTransactionAsync();
                var rowsAffected = await _context.Snapshots
                    .Where(s => s.Id == snapshotId)
                    .ExecuteDeleteAsync();

                if (rowsAffected > 0) {
                    await transaction.CommitAsync();
                    _logger.LogInformation("Deleted snapshot {SnapshotId}", snapshotId);
                }
                return rowsAffected > 0;
            }, $"Deleted snapshot {snapshotId}");
        }

        /// <summary>
        /// Updates the name of a snapshot.
        /// </summary>
        /// <param name="snapshotId">The ID of the snapshot.</param>
        /// <param name="newName">The new name for the snapshot.</param>
        /// <exception cref="ArgumentNullException">Thrown if newName is null or empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the snapshot is not found.</exception>
        public async Task UpdateSnapshotNameAsync(Guid snapshotId, string newName) {
            if (string.IsNullOrEmpty(newName)) throw new ArgumentNullException(nameof(newName));

            await ExecuteWithLockAsync(async () => {
                _context.ChangeTracker.Clear();
                using var transaction = await _context.Database.BeginTransactionAsync();
                var snapshot = await _context.Snapshots
                    .FirstOrDefaultAsync(s => s.Id == snapshotId);

                if (snapshot == null) {
                    _logger.LogError("Snapshot {SnapshotId} not found", snapshotId);
                    throw new InvalidOperationException($"Snapshot {snapshotId} not found");
                }

                snapshot.Name = newName;
                snapshot.Timestamp = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                _logger.LogInformation("Updated snapshot {SnapshotId} name to {NewName}", snapshotId, newName);
                return true;
            }, $"Updated snapshot name {snapshotId}");
        }

        /// <summary>
        /// Cleans up old updates for a document, keeping only the most recent ones or those within a time range.
        /// </summary>
        /// <param name="documentId">The ID of the document.</param>
        /// <param name="maxUpdates">The maximum number of updates to keep.</param>
        /// <param name="maxAge">The maximum age of updates to keep, if specified.</param>
        /// <returns>The number of updates deleted.</returns>
        /// <exception cref="ArgumentNullException">Thrown if documentId is null or empty.</exception>
        public async Task<int> CleanupOldUpdatesAsync(string documentId, int maxUpdates = 100, TimeSpan? maxAge = null) {
            ValidateDocumentId(documentId);
            return await ExecuteWithLockAsync(async () => {
                _context.ChangeTracker.Clear();
                using var transaction = await _context.Database.BeginTransactionAsync();
                var query = _context.Updates
                    .Where(x => x.DocumentId == documentId)
                    .OrderByDescending(x => x.Timestamp)
                    .Skip(maxUpdates);

                if (maxAge.HasValue) {
                    var cutoff = DateTime.UtcNow - maxAge.Value;
                    query = query.Where(x => x.Timestamp < cutoff);
                }

                var deletedCount = await query.ExecuteDeleteAsync();
                if (deletedCount > 0) {
                    await transaction.CommitAsync();
                    _logger.LogInformation("Cleaned up {Count} old updates for document {DocumentId}", deletedCount, documentId);
                }
                return deletedCount;
            }, $"Cleaned up old updates for document {documentId}");
        }

        /// <summary>
        /// Cleans up old updates for all documents.
        /// </summary>
        /// <param name="maxUpdatesPerDocument">The maximum number of updates to keep per document.</param>
        /// <param name="maxAge">The maximum age of updates to keep, if specified.</param>
        /// <returns>The total number of updates deleted.</returns>
        public async Task<int> CleanupAllDocumentsAsync(int maxUpdatesPerDocument = 100, TimeSpan? maxAge = null) {
            return await ExecuteWithLockAsync(async () => {
                _context.ChangeTracker.Clear();
                var documentIds = await _context.Documents
                    .AsNoTracking()
                    .Select(d => d.Id)
                    .ToListAsync();

                var totalDeleted = 0;
                foreach (var docId in documentIds) {
                    totalDeleted += await CleanupOldUpdatesAsync(docId, maxUpdatesPerDocument, maxAge);
                }

                _logger.LogInformation("Cleaned up {TotalDeleted} updates across {DocumentCount} documents", totalDeleted, documentIds.Count);
                return totalDeleted;
            }, "Cleaned up all documents");
        }

        /// <summary>
        /// Creates multiple document updates in a batch.
        /// </summary>
        /// <param name="updates">The collection of updates to create, each with document ID, type, and data.</param>
        /// <returns>A list of created document updates.</returns>
        /// <exception cref="ArgumentNullException">Thrown if updates is null.</exception>
        public async Task<List<DBDocumentUpdate>> CreateUpdatesAsync(IEnumerable<(string documentId, string type, byte[] update)> updates) {
            if (updates == null) throw new ArgumentNullException(nameof(updates));

            var dbUpdates = new List<DBDocumentUpdate>();
            var timestamp = DateTime.UtcNow;
            var clientId = Guid.NewGuid();

            foreach (var (documentId, type, update) in updates) {
                if (string.IsNullOrEmpty(documentId) || string.IsNullOrEmpty(type) || update == null) {
                    _logger.LogWarning("Skipping invalid update: DocumentId={DocumentId}, Type={Type}, UpdateSize={UpdateSize}", documentId, type, update?.Length ?? 0);
                    continue;
                }

                dbUpdates.Add(new DBDocumentUpdate {
                    DocumentId = documentId,
                    Type = type,
                    Data = update,
                    Timestamp = timestamp,
                    Id = Guid.NewGuid(),
                    ClientId = clientId
                });
            }

            if (dbUpdates.Any()) {
                await ExecuteWithLockAsync(async () => {
                    _context.ChangeTracker.Clear();
                    using var transaction = await _context.Database.BeginTransactionAsync();
                    const int batchSize = 1000;
                    for (int i = 0; i < dbUpdates.Count; i += batchSize) {
                        var batch = dbUpdates.Skip(i).Take(batchSize).ToList();
                        _context.Updates.AddRange(batch);
                        await _context.SaveChangesAsync();
                    }
                    await transaction.CommitAsync();
                    _logger.LogInformation("Created batch of {Count} updates", dbUpdates.Count);
                    return true;
                }, $"Created batch of {dbUpdates.Count} updates");
            }

            return dbUpdates;
        }

        /// <summary>
        /// Disposes the service and its resources.
        /// </summary>
        public void Dispose() {
            if (_disposed) return;
            _context?.Dispose();
            _contextLock.Dispose();
            _disposed = true;
        }

        private async Task<T> ExecuteWithLockAsync<T>(Func<Task<T>> operation, string successMessage) {
            await _contextLock.WaitAsync();
            try {
                var result = await operation();
                _logger.LogDebug("{Message}", successMessage);
                return result;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Operation failed: {Message}", successMessage);
                throw;
            }
            finally {
                _contextLock.Release();
            }
        }

        private async Task ExecuteWithLockAsync(Func<Task> operation, string successMessage) {
            await _contextLock.WaitAsync();
            try {
                await operation();
                _logger.LogDebug("{Message}", successMessage);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Operation failed: {Message}", successMessage);
                throw;
            }
            finally {
                _contextLock.Release();
            }
        }

        private static void ValidateDocumentId(string documentId) {
            if (string.IsNullOrEmpty(documentId))
                throw new ArgumentNullException(nameof(documentId));
        }
    }
}