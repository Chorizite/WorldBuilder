using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Documents {
    public class FileStorageService : IDocumentStorageService {
        private readonly string _storageDirectory;
        private readonly ILogger<DocumentStorageService> _logger;
        private readonly object _fileLock = new object();

        public FileStorageService(string storageDirectory, ILogger<DocumentStorageService> logger) {
            _storageDirectory = storageDirectory ?? throw new ArgumentNullException(nameof(storageDirectory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Ensure storage directory exists
            Directory.CreateDirectory(_storageDirectory);
        }

        private string GetDocumentFilePath(string documentId) =>
            Path.Combine(_storageDirectory, $"{documentId}.doc");

        private string GetUpdatesFilePath(string documentId) =>
            Path.Combine(_storageDirectory, $"{documentId}.updates");

        public async Task<DBDocument?> GetDocumentAsync(string documentId) {
            if (string.IsNullOrEmpty(documentId)) throw new ArgumentNullException(nameof(documentId));

            var filePath = GetDocumentFilePath(documentId);
            if (!File.Exists(filePath)) return null;

            try {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new BinaryReader(stream);

                var typeLength = reader.ReadInt32();
                var type = reader.ReadString();
                var lastModifiedTicks = reader.ReadInt64();
                var dataLength = reader.ReadInt32();
                var data = reader.ReadBytes(dataLength);

                return new DBDocument {
                    Id = documentId,
                    Type = type,
                    LastModified = new DateTime(lastModifiedTicks, DateTimeKind.Utc),
                    Data = data
                };
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to read document {DocumentId}", documentId);
                return null;
            }
        }

        public async Task<DBDocument> CreateDocumentAsync(string documentId, string type, byte[] initialData) {
            if (string.IsNullOrEmpty(documentId)) throw new ArgumentNullException(nameof(documentId));
            if (string.IsNullOrEmpty(type)) throw new ArgumentNullException(nameof(type));
            if (initialData == null) throw new ArgumentNullException(nameof(initialData));

            var filePath = GetDocumentFilePath(documentId);
            var document = new DBDocument {
                Id = documentId,
                Type = type,
                Data = initialData,
                LastModified = DateTime.UtcNow
            };

            lock (_fileLock) {
                if (File.Exists(filePath))
                    throw new InvalidOperationException($"Document {documentId} already exists");
            }

            try {
                using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                using var writer = new BinaryWriter(stream);

                writer.Write(type.Length);
                writer.Write(type);
                writer.Write(document.LastModified.Ticks);
                writer.Write(initialData.Length);
                writer.Write(initialData);

                _logger.LogInformation("Created document {DocumentId} of type {Type} ({Size} bytes)",
                    documentId, type, initialData.Length);

                return document;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to create document {DocumentId}", documentId);
                throw;
            }
        }

        public async Task<DBDocument> UpdateDocumentAsync(string documentId, byte[] update) {
            if (string.IsNullOrEmpty(documentId)) throw new ArgumentNullException(nameof(documentId));
            if (update == null) throw new ArgumentNullException(nameof(update));

            var filePath = GetDocumentFilePath(documentId);
            lock (_fileLock) {
                if (!File.Exists(filePath))
                    throw new InvalidOperationException($"Document {documentId} not found");
            }

            try {
                // Read existing document to preserve type
                DBDocument? existing = await GetDocumentAsync(documentId);
                if (existing == null)
                    throw new InvalidOperationException($"Document {documentId} not found");

                var now = DateTime.UtcNow;
                using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                using var writer = new BinaryWriter(stream);

                writer.Write(existing.Type.Length);
                writer.Write(existing.Type);
                writer.Write(now.Ticks);
                writer.Write(update.Length);
                writer.Write(update);

                _logger.LogDebug("Updated document {DocumentId} ({Size} bytes)", documentId, update.Length);

                return new DBDocument {
                    Id = documentId,
                    Type = existing.Type,
                    Data = update,
                    LastModified = now
                };
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to update document {DocumentId}", documentId);
                throw;
            }
        }

        public async Task<bool> DeleteDocumentAsync(string documentId) {
            if (string.IsNullOrEmpty(documentId)) throw new ArgumentNullException(nameof(documentId));

            var docPath = GetDocumentFilePath(documentId);
            var updatesPath = GetUpdatesFilePath(documentId);

            bool deleted = false;
            lock (_fileLock) {
                if (File.Exists(docPath)) {
                    File.Delete(docPath);
                    deleted = true;
                }
                if (File.Exists(updatesPath)) {
                    File.Delete(updatesPath);
                }
            }

            if (deleted) {
                _logger.LogInformation("Deleted document {DocumentId}", documentId);
            }

            return deleted;
        }

        public async Task<DBDocumentUpdate> CreateUpdateAsync(string documentId, string type, byte[] update) {
            if (string.IsNullOrEmpty(documentId)) throw new ArgumentNullException(nameof(documentId));
            if (string.IsNullOrEmpty(type)) throw new ArgumentNullException(nameof(type));
            if (update == null) throw new ArgumentNullException(nameof(update));

            var updatesPath = GetUpdatesFilePath(documentId);
            var dbUpdate = new DBDocumentUpdate {
                DocumentId = documentId,
                Type = type,
                Data = update,
                Timestamp = DateTime.UtcNow,
                Id = Guid.NewGuid(),
                ClientId = Guid.NewGuid()
            };

            try {
                lock (_fileLock) {
                    using var stream = new FileStream(updatesPath, FileMode.Append, FileAccess.Write, FileShare.None);
                    using var writer = new BinaryWriter(stream);

                    writer.Write(dbUpdate.Id.ToByteArray());
                    writer.Write(dbUpdate.ClientId.ToByteArray());
                    writer.Write(dbUpdate.Timestamp.Ticks);
                    writer.Write(type.Length);
                    writer.Write(type);
                    writer.Write(update.Length);
                    writer.Write(update);
                }

                return dbUpdate;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to create update for document {DocumentId}", documentId);
                throw;
            }
        }

        public async Task<List<DBDocumentUpdate>> GetDocumentUpdatesAsync(string documentId) {
            if (string.IsNullOrEmpty(documentId)) throw new ArgumentNullException(nameof(documentId));

            var updatesPath = GetUpdatesFilePath(documentId);
            var updates = new List<DBDocumentUpdate>();

            if (!File.Exists(updatesPath)) return updates;

            try {
                using var stream = new FileStream(updatesPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new BinaryReader(stream);

                while (stream.Position < stream.Length) {
                    var id = new Guid(reader.ReadBytes(16));
                    var clientId = new Guid(reader.ReadBytes(16));
                    var timestampTicks = reader.ReadInt64();
                    var typeLength = reader.ReadInt32();
                    var type = reader.ReadString();
                    var dataLength = reader.ReadInt32();
                    var data = reader.ReadBytes(dataLength);

                    updates.Add(new DBDocumentUpdate {
                        Id = id,
                        ClientId = clientId,
                        DocumentId = documentId,
                        Timestamp = new DateTime(timestampTicks, DateTimeKind.Utc),
                        Type = type,
                        Data = data
                    });
                }

                return updates.OrderBy(x => x.Timestamp).ToList();
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to read updates for document {DocumentId}", documentId);
                return updates;
            }
        }

        public async Task<List<DBDocumentUpdate>> CreateUpdatesAsync(IEnumerable<(string documentId, string type, byte[] update)> updates) {
            var result = new List<DBDocumentUpdate>();
            var timestamp = DateTime.UtcNow;
            var clientId = Guid.NewGuid();

            foreach (var (documentId, type, update) in updates) {
                if (string.IsNullOrEmpty(documentId) || string.IsNullOrEmpty(type) || update == null) {
                    _logger.LogWarning("Skipping invalid update: DocumentId={DocumentId}, Type={Type}, UpdateSize={UpdateSize}",
                        documentId, type, update?.Length ?? 0);
                    continue;
                }

                var dbUpdate = await CreateUpdateAsync(documentId, type, update);
                dbUpdate.ClientId = clientId;
                dbUpdate.Timestamp = timestamp;
                result.Add(dbUpdate);
            }

            if (result.Any()) {
                _logger.LogInformation("Created batch of {Count} updates", result.Count);
            }

            return result;
        }

        public async Task<int> CleanupOldUpdatesAsync(string documentId, int maxUpdates = 100, TimeSpan? maxAge = null) {
            if (string.IsNullOrEmpty(documentId)) throw new ArgumentNullException(nameof(documentId));

            var updates = await GetDocumentUpdatesAsync(documentId);
            if (!updates.Any()) return 0;

            var toKeep = updates.OrderByDescending(x => x.Timestamp)
                .Take(maxUpdates);

            if (maxAge.HasValue) {
                var cutoff = DateTime.UtcNow - maxAge.Value;
                toKeep = toKeep.Where(x => x.Timestamp >= cutoff);
            }

            var toKeepList = toKeep.ToList();
            if (toKeepList.Count == updates.Count) return 0;

            var updatesPath = GetUpdatesFilePath(documentId);
            lock (_fileLock) {
                using var stream = new FileStream(updatesPath, FileMode.Create, FileAccess.Write, FileShare.None);
                using var writer = new BinaryWriter(stream);

                foreach (var update in toKeepList) {
                    writer.Write(update.Id.ToByteArray());
                    writer.Write(update.ClientId.ToByteArray());
                    writer.Write(update.Timestamp.Ticks);
                    writer.Write(update.Type.Length);
                    writer.Write(update.Type);
                    writer.Write(update.Data.Length);
                    writer.Write(update.Data);
                }
            }

            var deletedCount = updates.Count - toKeepList.Count;
            if (deletedCount > 0) {
                _logger.LogInformation("Cleaned up {Count} old updates for document {DocumentId}", deletedCount, documentId);
            }

            return deletedCount;
        }

        public async Task<int> CleanupAllDocumentsAsync(int maxUpdatesPerDocument = 100, TimeSpan? maxAge = null) {
            var documentIds = Directory.GetFiles(_storageDirectory, "*.doc")
                .Select(Path.GetFileNameWithoutExtension)
                .ToList();

            int totalDeleted = 0;
            foreach (var docId in documentIds) {
                totalDeleted += await CleanupOldUpdatesAsync(docId, maxUpdatesPerDocument, maxAge);
            }

            _logger.LogInformation("Cleaned up {TotalDeleted} updates across {DocumentCount} documents",
                totalDeleted, documentIds.Count);

            return totalDeleted;
        }

        public void Dispose() {
            // No resources to dispose
        }
    }
}