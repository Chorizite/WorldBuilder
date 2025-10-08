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
    public class DocumentStorageService : IDocumentStorageService {
        private readonly DocumentDbContext _context;
        private readonly ILogger<DocumentStorageService> _logger;
        private readonly SemaphoreSlim _contextLock = new SemaphoreSlim(1, 1);

        public DocumentStorageService(DocumentDbContext context, ILogger<DocumentStorageService> logger) {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<DBDocument?> GetDocumentAsync(string documentId) {
            if (string.IsNullOrEmpty(documentId)) throw new ArgumentNullException(nameof(documentId));

            await _contextLock.WaitAsync();
            try {
                return await _context.Documents
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == documentId);
            }
            finally {
                _contextLock.Release();
            }
        }

        public async Task<DBDocument> CreateDocumentAsync(string documentId, string type, byte[] initialData) {
            if (string.IsNullOrEmpty(documentId)) throw new ArgumentNullException(nameof(documentId));
            if (string.IsNullOrEmpty(type)) throw new ArgumentNullException(nameof(type));
            if (initialData == null) throw new ArgumentNullException(nameof(initialData));

            var document = new DBDocument {
                Id = documentId,
                Type = type,
                Data = initialData,
                LastModified = DateTime.UtcNow
            };

            await _contextLock.WaitAsync();
            try {
                using var transaction = await _context.Database.BeginTransactionAsync();
                _context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
                _context.ChangeTracker.AutoDetectChangesEnabled = true;

                _context.Documents.Add(document);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("Created document {DocumentId} of type {Type} ({Size} bytes)",
                    document.Id, type, initialData.Length);

                return document;
            }
            finally {
                _context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
                _context.ChangeTracker.AutoDetectChangesEnabled = false;
                _contextLock.Release();
            }
        }

        public async Task<DBDocument> UpdateDocumentAsync(string documentId, byte[] update) {
            await _contextLock.WaitAsync();
            try {
                var now = DateTime.UtcNow;
                using var transaction = await _context.Database.BeginTransactionAsync();

                var document = await _context.Documents
                    .FirstOrDefaultAsync(d => d.Id == documentId);

                if (document == null) {
                    throw new InvalidOperationException($"Document {documentId} not found");
                }

                document.Data = update;
                document.LastModified = now;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogDebug("Updated document {DocumentId} ({Size} bytes)", documentId, update.Length);

                return document;
            }
            finally {
                _contextLock.Release();
            }
        }

        public async Task<bool> DeleteDocumentAsync(string documentId) {
            await _contextLock.WaitAsync();
            try {
                using var transaction = await _context.Database.BeginTransactionAsync();

                var rowsAffected = await _context.Documents
                    .Where(d => d.Id == documentId)
                    .ExecuteDeleteAsync();

                if (rowsAffected > 0) {
                    _logger.LogInformation("Deleted document {DocumentId}", documentId);
                    await transaction.CommitAsync();
                }

                return rowsAffected > 0;
            }
            finally {
                _contextLock.Release();
            }
        }

        public async Task<DBDocumentUpdate> CreateUpdateAsync(string documentId, string type, byte[] update) {
            if (string.IsNullOrEmpty(documentId)) throw new ArgumentNullException(nameof(documentId));
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

            await _contextLock.WaitAsync();
            try {
                using var transaction = await _context.Database.BeginTransactionAsync();
                _context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
                _context.ChangeTracker.AutoDetectChangesEnabled = true;

                _context.Updates.Add(dbUpdate);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return dbUpdate;
            }
            finally {
                _context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
                _context.ChangeTracker.AutoDetectChangesEnabled = false;
                _contextLock.Release();
            }
        }

        public async Task<List<DBDocumentUpdate>> GetDocumentUpdatesAsync(string documentId) {
            if (string.IsNullOrEmpty(documentId)) throw new ArgumentNullException(nameof(documentId));

            await _contextLock.WaitAsync();
            try {
                return await _context.Updates
                    .AsNoTracking()
                    .Where(x => x.DocumentId == documentId)
                    .OrderBy(x => x.Timestamp)
                    .ToListAsync();
            }
            finally {
                _contextLock.Release();
            }
        }

        public async Task<DBSnapshot> CreateSnapshotAsync(DBSnapshot snapshot) {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (string.IsNullOrEmpty(snapshot.DocumentId)) throw new ArgumentNullException(nameof(snapshot.DocumentId));
            if (snapshot.Data == null) throw new ArgumentNullException(nameof(snapshot.Data));

            await _contextLock.WaitAsync();
            try {
                using var transaction = await _context.Database.BeginTransactionAsync();
                _context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
                _context.ChangeTracker.AutoDetectChangesEnabled = true;

                _context.Snapshots.Add(snapshot);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("Created snapshot {SnapshotId} for document {DocumentId} ({Size} bytes)",
                    snapshot.Id, snapshot.DocumentId, snapshot.Data.Length);

                return snapshot;
            }
            finally {
                _context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
                _context.ChangeTracker.AutoDetectChangesEnabled = false;
                _contextLock.Release();
            }
        }

        public async Task<DBSnapshot?> GetSnapshotAsync(Guid snapshotId) {
            await _contextLock.WaitAsync();
            try {
                return await _context.Snapshots
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == snapshotId);
            }
            finally {
                _contextLock.Release();
            }
        }

        public async Task<List<DBSnapshot>> GetSnapshotsAsync(string documentId) {
            if (string.IsNullOrEmpty(documentId)) throw new ArgumentNullException(nameof(documentId));

            await _contextLock.WaitAsync();
            try {
                return await _context.Snapshots
                    .AsNoTracking()
                    .Where(s => s.DocumentId == documentId)
                    .OrderBy(s => s.Timestamp)
                    .ToListAsync();
            }
            finally {
                _contextLock.Release();
            }
        }

        public async Task<bool> DeleteSnapshotAsync(Guid snapshotId) {
            await _contextLock.WaitAsync();
            try {
                using var transaction = await _context.Database.BeginTransactionAsync();

                var rowsAffected = await _context.Snapshots
                    .Where(s => s.Id == snapshotId)
                    .ExecuteDeleteAsync();

                if (rowsAffected > 0) {
                    _logger.LogInformation("Deleted snapshot {SnapshotId}", snapshotId);
                    await transaction.CommitAsync();
                }

                return rowsAffected > 0;
            }
            finally {
                _contextLock.Release();
            }
        }

        public async Task UpdateSnapshotNameAsync(Guid snapshotId, string newName) {
            if (string.IsNullOrEmpty(newName)) throw new ArgumentNullException(nameof(newName));

            await _contextLock.WaitAsync();
            try {
                using var transaction = await _context.Database.BeginTransactionAsync();

                var snapshot = await _context.Snapshots
                    .FirstOrDefaultAsync(s => s.Id == snapshotId);

                if (snapshot == null) {
                    throw new InvalidOperationException($"Snapshot {snapshotId} not found");
                }

                snapshot.Name = newName;
                snapshot.Timestamp = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogDebug("Updated snapshot {SnapshotId} name to {NewName}", snapshotId, newName);
            }
            finally {
                _contextLock.Release();
            }
        }

        public async Task<int> CleanupOldUpdatesAsync(string documentId, int maxUpdates = 100, TimeSpan? maxAge = null) {
            if (string.IsNullOrEmpty(documentId)) throw new ArgumentNullException(nameof(documentId));

            await _contextLock.WaitAsync();
            try {
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
                    _logger.LogInformation("Cleaned up {Count} old updates for document {DocumentId}", deletedCount, documentId);
                    await transaction.CommitAsync();
                }

                return deletedCount;
            }
            finally {
                _contextLock.Release();
            }
        }

        public async Task<int> CleanupAllDocumentsAsync(int maxUpdatesPerDocument = 100, TimeSpan? maxAge = null) {
            await _contextLock.WaitAsync();
            try {
                var documentIds = await _context.Documents
                    .AsNoTracking()
                    .Select(d => d.Id)
                    .ToListAsync();

                var totalDeleted = 0;
                foreach (var docId in documentIds) {
                    totalDeleted += await CleanupOldUpdatesAsync(docId, maxUpdatesPerDocument, maxAge);
                }

                _logger.LogInformation("Cleaned up {TotalDeleted} updates across {DocumentCount} documents",
                    totalDeleted, documentIds.Count);

                return totalDeleted;
            }
            finally {
                _contextLock.Release();
            }
        }

        public async Task<List<DBDocumentUpdate>> CreateUpdatesAsync(IEnumerable<(string documentId, string type, byte[] update)> updates) {
            var dbUpdates = new List<DBDocumentUpdate>();
            var timestamp = DateTime.UtcNow;
            var clientId = Guid.NewGuid();

            foreach (var (documentId, type, update) in updates) {
                if (string.IsNullOrEmpty(documentId) || string.IsNullOrEmpty(type) || update == null) {
                    _logger.LogWarning("Skipping invalid update: DocumentId={DocumentId}, Type={Type}, UpdateSize={UpdateSize}",
                        documentId, type, update?.Length ?? 0);
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
                await _contextLock.WaitAsync();
                try {
                    using var transaction = await _context.Database.BeginTransactionAsync();
                    const int batchSize = 1000;

                    for (int i = 0; i < dbUpdates.Count; i += batchSize) {
                        var batch = dbUpdates.Skip(i).Take(batchSize).ToList();
                        await _context.BulkInsertUpdatesAsync(batch);
                    }
                    await transaction.CommitAsync();
                    _logger.LogInformation("Created batch of {Count} updates", dbUpdates.Count);
                }
                catch (Exception ex) {
                    _logger.LogError(ex, "Failed to create batch of updates");
                    throw;
                }
                finally {
                    _contextLock.Release();
                }
            }

            return dbUpdates;
        }

        public void Dispose() {
            _context?.Dispose();
            _contextLock.Dispose();
        }
    }
}