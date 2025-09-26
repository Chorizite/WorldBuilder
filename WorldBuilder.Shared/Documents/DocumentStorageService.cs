using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Documents {
    public class DocumentStorageService : IDocumentStorageService {
        private readonly DocumentDbContext _context;
        private readonly ILogger<DocumentStorageService> _logger;

        public DocumentStorageService(DocumentDbContext context, ILogger<DocumentStorageService> logger) {
            _context = context;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<DBDocument?> GetDocumentAsync(string documentId) {
            if (string.IsNullOrEmpty(documentId)) throw new ArgumentNullException(nameof(documentId));

            return await _context.Documents
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == documentId);
        }

        /// <inheritdoc />
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

            // Temporarily enable change tracking for this operation
            var originalTracking = _context.ChangeTracker.QueryTrackingBehavior;
            var originalAutoDetect = _context.ChangeTracker.AutoDetectChangesEnabled;

            try {
                _context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
                _context.ChangeTracker.AutoDetectChangesEnabled = true;

                _context.Documents.Add(document);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Created document {DocumentId} of type {Type} ({Size} bytes)",
                    document.Id, type, initialData.Length);

                return document;
            }
            finally {
                _context.ChangeTracker.QueryTrackingBehavior = originalTracking;
                _context.ChangeTracker.AutoDetectChangesEnabled = originalAutoDetect;
            }
        }

        /// <inheritdoc />
        public async Task<DBDocument> UpdateDocumentAsync(string documentId, byte[] update) {
            var now = DateTime.UtcNow;

            // Use ExecuteUpdate for better performance - no entity loading required
            var rowsAffected = await _context.Documents
                .Where(d => d.Id == documentId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(d => d.Data, update)
                    .SetProperty(d => d.LastModified, now));

            if (rowsAffected == 0) {
                throw new InvalidOperationException($"Document {documentId} not found");
            }

            _logger.LogDebug("Updated document {DocumentId} ({Size} bytes)", documentId, update.Length);

            // Return a minimal document object since we're not tracking
            return new DBDocument {
                Id = documentId,
                Data = update,
                LastModified = now
            };
        }

        /// <inheritdoc />
        public async Task<bool> DeleteDocumentAsync(string documentId) {
            // Use ExecuteDelete for better performance
            var rowsAffected = await _context.Documents
                .Where(d => d.Id == documentId)
                .ExecuteDeleteAsync();

            if (rowsAffected > 0) {
                _logger.LogInformation("Deleted document {DocumentId}", documentId);
            }

            return rowsAffected > 0;
        }

        /// <inheritdoc />
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
                ClientId = Guid.NewGuid() // This should probably come from the calling context
            };

            // Temporarily enable change tracking for this operation
            var originalTracking = _context.ChangeTracker.QueryTrackingBehavior;
            var originalAutoDetect = _context.ChangeTracker.AutoDetectChangesEnabled;

            try {
                _context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
                _context.ChangeTracker.AutoDetectChangesEnabled = true;

                _context.Updates.Add(dbUpdate);
                await _context.SaveChangesAsync();

                return dbUpdate;
            }
            finally {
                _context.ChangeTracker.QueryTrackingBehavior = originalTracking;
                _context.ChangeTracker.AutoDetectChangesEnabled = originalAutoDetect;
            }
        }

        /// <inheritdoc />
        public async Task<List<DBDocumentUpdate>> GetDocumentUpdatesAsync(string documentId) {
            if (string.IsNullOrEmpty(documentId)) throw new ArgumentNullException(nameof(documentId));

            return await _context.Updates
                .AsNoTracking()
                .Where(x => x.DocumentId == documentId)
                .OrderBy(x => x.Timestamp)
                .ToListAsync();
        }

        /// <summary>
        /// Batch create multiple updates in a single transaction for better performance
        /// </summary>
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
                const int batchSize = 1000; // Adjust based on DB performance
                for (int i = 0; i < dbUpdates.Count; i += batchSize) {
                    var batch = dbUpdates.Skip(i).Take(batchSize).ToList();
                    await _context.BulkInsertUpdatesAsync(batch);
                }
                _logger.LogInformation("Created batch of {Count} updates", dbUpdates.Count);
            }

            return dbUpdates;
        }

        /// <summary>
        /// Clean up old updates beyond a certain age or count
        /// </summary>
        public async Task<int> CleanupOldUpdatesAsync(string documentId, int maxUpdates = 100, TimeSpan? maxAge = null) {
            if (string.IsNullOrEmpty(documentId)) throw new ArgumentNullException(nameof(documentId));

            // Delete updates beyond maxUpdates, keeping the most recent ones
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
            }

            return deletedCount;
        }

        /// <summary>
        /// Get statistics about a document's update history
        /// </summary>
        public async Task<DocumentStats> GetDocumentStatsAsync(string documentId) {
            return await _context.GetDocumentStatsAsync(documentId);
        }

        /// <summary>
        /// Batch cleanup for all documents
        /// </summary>
        public async Task<int> CleanupAllDocumentsAsync(int maxUpdatesPerDocument = 100, TimeSpan? maxAge = null) {
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

        /// <inheritdoc />
        public void Dispose() {
           
        }
    }
}