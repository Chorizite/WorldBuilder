using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Documents {
    /// <summary>
    /// Manages database operations for documents, updates, and snapshots using raw SQL via ADO.NET.
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

        public async Task<DBDocument?> GetDocumentAsync(string documentId) {
            ValidateDocumentId(documentId);
            return await ExecuteWithLockAsync(async () => {
                var conn = GetOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Id, Type, Data, LastModified FROM Documents WHERE Id = @Id";
                AddParameter(cmd, "@Id", documentId);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync()) {
                    return MapDocument(reader);
                }
                return null;
            }, $"Retrieved document {documentId}");
        }

        public async Task<DBDocument> CreateDocumentAsync(string documentId, string type, byte[] initialData) {
            ValidateDocumentId(documentId);
            if (string.IsNullOrEmpty(type)) throw new ArgumentNullException(nameof(type));
            if (initialData == null) throw new ArgumentNullException(nameof(initialData));

            return await ExecuteWithLockAsync(async () => {
                var conn = GetOpenConnection();
                using var transaction = conn.BeginTransaction();
                using var cmd = conn.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = "INSERT INTO Documents (Id, Type, Data, LastModified) VALUES (@Id, @Type, @Data, @LastModified)";
                AddParameter(cmd, "@Id", documentId);
                AddParameter(cmd, "@Type", type);
                AddParameter(cmd, "@Data", initialData);
                AddParameter(cmd, "@LastModified", DateTime.UtcNow);

                await cmd.ExecuteNonQueryAsync();
                transaction.Commit();

                _logger.LogInformation("Created document {DocumentId} of type {Type} ({Size} bytes)", documentId, type, initialData.Length);
                return new DBDocument { Id = documentId, Type = type, Data = initialData, LastModified = DateTime.UtcNow };
            }, $"Created document {documentId}");
        }

        public async Task<DBDocument> UpdateDocumentAsync(string documentId, byte[] update) {
            ValidateDocumentId(documentId);
            if (update == null) throw new ArgumentNullException(nameof(update));

            return await ExecuteWithLockAsync(async () => {
                var conn = GetOpenConnection();
                using var transaction = conn.BeginTransaction();

                // Fetch existing for type validation
                string existingType = await GetExistingTypeAsync(conn, transaction, documentId);
                if (existingType == null) {
                    throw new InvalidOperationException($"Document {documentId} not found");
                }

                // Update
                using var cmd = conn.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = "UPDATE Documents SET Data = @Data, LastModified = @LastModified WHERE Id = @Id";
                AddParameter(cmd, "@Data", update);
                AddParameter(cmd, "@LastModified", DateTime.UtcNow);
                AddParameter(cmd, "@Id", documentId);

                var changes = await cmd.ExecuteNonQueryAsync();
                if (changes == 0) throw new InvalidOperationException($"Document {documentId} not found during update");

                transaction.Commit();
                _logger.LogInformation("Updated document {DocumentId} ({Size} bytes)", documentId, update.Length);
                return new DBDocument { Id = documentId, Type = existingType, Data = update, LastModified = DateTime.UtcNow };
            }, $"Updated document {documentId}");
        }

        public async Task<bool> DeleteDocumentAsync(string documentId) {
            ValidateDocumentId(documentId);
            return await ExecuteWithLockAsync(async () => {
                var conn = GetOpenConnection();
                using var transaction = conn.BeginTransaction();
                using var cmd = conn.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM Documents WHERE Id = @Id";
                AddParameter(cmd, "@Id", documentId);

                var rowsAffected = await cmd.ExecuteNonQueryAsync();
                if (rowsAffected > 0) {
                    transaction.Commit();
                    _logger.LogInformation("Deleted document {DocumentId}", documentId);
                }
                return rowsAffected > 0;
            }, $"Deleted document {documentId}");
        }

        public async Task<DBDocumentUpdate> CreateUpdateAsync(string documentId, string type, byte[] update) {
            ValidateDocumentId(documentId);
            if (string.IsNullOrEmpty(type)) throw new ArgumentNullException(nameof(type));
            if (update == null) throw new ArgumentNullException(nameof(update));

            var dbUpdate = new DBDocumentUpdate {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                Type = type,
                Data = update,
                Timestamp = DateTime.UtcNow,
                ClientId = Guid.NewGuid()
            };

            return await ExecuteWithLockAsync(async () => {
                var conn = GetOpenConnection();
                using var transaction = conn.BeginTransaction();
                using var cmd = conn.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = "INSERT INTO Updates (Id, DocumentId, Type, Data, Timestamp, ClientId) VALUES (@Id, @DocumentId, @Type, @Data, @Timestamp, @ClientId)";
                AddParameter(cmd, "@Id", dbUpdate.Id);
                AddParameter(cmd, "@DocumentId", dbUpdate.DocumentId);
                AddParameter(cmd, "@Type", dbUpdate.Type);
                AddParameter(cmd, "@Data", dbUpdate.Data);
                AddParameter(cmd, "@Timestamp", dbUpdate.Timestamp);
                AddParameter(cmd, "@ClientId", dbUpdate.ClientId);

                await cmd.ExecuteNonQueryAsync();
                transaction.Commit();

                _logger.LogInformation("Created update for document {DocumentId} ({Size} bytes)", documentId, update.Length);
                return dbUpdate;
            }, $"Created update for document {documentId}");
        }

        public async Task<List<DBDocumentUpdate>> GetDocumentUpdatesAsync(string documentId) {
            ValidateDocumentId(documentId);
            return await ExecuteWithLockAsync(async () => {
                var conn = GetOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Id, DocumentId, Type, Data, Timestamp, ClientId FROM Updates WHERE DocumentId = @DocumentId ORDER BY Timestamp";
                AddParameter(cmd, "@DocumentId", documentId);

                using var reader = await cmd.ExecuteReaderAsync();
                var updates = new List<DBDocumentUpdate>();
                while (await reader.ReadAsync()) {
                    updates.Add(MapUpdate(reader));
                }
                return updates;
            }, $"Retrieved updates for document {documentId}");
        }

        public async Task<DBSnapshot> CreateSnapshotAsync(DBSnapshot snapshot) {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (string.IsNullOrEmpty(snapshot.DocumentId)) throw new ArgumentNullException(nameof(snapshot.DocumentId));
            if (snapshot.Data == null) throw new ArgumentNullException(nameof(snapshot.Data));

            if (snapshot.Id == Guid.Empty) snapshot.Id = Guid.NewGuid();
            snapshot.Timestamp = DateTime.UtcNow;

            return await ExecuteWithLockAsync(async () => {
                var conn = GetOpenConnection();
                using var transaction = conn.BeginTransaction();
                using var cmd = conn.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = "INSERT INTO Snapshots (Id, DocumentId, Name, Data, Timestamp) VALUES (@Id, @DocumentId, @Name, @Data, @Timestamp)";
                AddParameter(cmd, "@Id", snapshot.Id);
                AddParameter(cmd, "@DocumentId", snapshot.DocumentId);
                AddParameter(cmd, "@Name", snapshot.Name ?? (object)DBNull.Value);
                AddParameter(cmd, "@Data", snapshot.Data);
                AddParameter(cmd, "@Timestamp", snapshot.Timestamp);

                await cmd.ExecuteNonQueryAsync();
                transaction.Commit();

                _logger.LogInformation("Created snapshot {SnapshotId} for document {DocumentId} ({Size} bytes)", snapshot.Id, snapshot.DocumentId, snapshot.Data.Length);
                return snapshot;
            }, $"Created snapshot for document {snapshot.DocumentId}");
        }

        public async Task<DBSnapshot?> GetSnapshotAsync(Guid snapshotId) {
            return await ExecuteWithLockAsync(async () => {
                var conn = GetOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Id, DocumentId, Name, Data, Timestamp FROM Snapshots WHERE Id = @Id";
                AddParameter(cmd, "@Id", snapshotId);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync()) {
                    return MapSnapshot(reader);
                }
                return null;
            }, $"Retrieved snapshot {snapshotId}");
        }

        public async Task<List<DBSnapshot>> GetSnapshotsAsync(string documentId) {
            ValidateDocumentId(documentId);
            return await ExecuteWithLockAsync(async () => {
                var conn = GetOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Id, DocumentId, Name, Data, Timestamp FROM Snapshots WHERE DocumentId = @DocumentId ORDER BY Timestamp";
                AddParameter(cmd, "@DocumentId", documentId);

                using var reader = await cmd.ExecuteReaderAsync();
                var snapshots = new List<DBSnapshot>();
                while (await reader.ReadAsync()) {
                    snapshots.Add(MapSnapshot(reader));
                }
                return snapshots;
            }, $"Retrieved snapshots for document {documentId}");
        }

        public async Task<bool> DeleteSnapshotAsync(Guid snapshotId) {
            return await ExecuteWithLockAsync(async () => {
                var conn = GetOpenConnection();
                using var transaction = conn.BeginTransaction();
                using var cmd = conn.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM Snapshots WHERE Id = @Id";
                AddParameter(cmd, "@Id", snapshotId);

                var rowsAffected = await cmd.ExecuteNonQueryAsync();
                if (rowsAffected > 0) {
                    transaction.Commit();
                    _logger.LogInformation("Deleted snapshot {SnapshotId}", snapshotId);
                }
                return rowsAffected > 0;
            }, $"Deleted snapshot {snapshotId}");
        }

        public async Task UpdateSnapshotNameAsync(Guid snapshotId, string newName) {
            if (string.IsNullOrEmpty(newName)) throw new ArgumentNullException(nameof(newName));

            await ExecuteWithLockAsync(async () => {
                var conn = GetOpenConnection();
                using var transaction = conn.BeginTransaction();

                // Check existence
                using var checkCmd = conn.CreateCommand();
                checkCmd.Transaction = transaction;
                checkCmd.CommandText = "SELECT 1 FROM Snapshots WHERE Id = @Id";
                AddParameter(checkCmd, "@Id", snapshotId);
                var exists = await checkCmd.ExecuteScalarAsync();
                if (exists == null) throw new InvalidOperationException($"Snapshot {snapshotId} not found");

                // Update
                using var cmd = conn.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = "UPDATE Snapshots SET Name = @Name, Timestamp = @Timestamp WHERE Id = @Id";
                AddParameter(cmd, "@Name", newName);
                AddParameter(cmd, "@Timestamp", DateTime.UtcNow);
                AddParameter(cmd, "@Id", snapshotId);

                await cmd.ExecuteNonQueryAsync();
                transaction.Commit();

                _logger.LogInformation("Updated snapshot {SnapshotId} name to {NewName}", snapshotId, newName);
            }, $"Updated snapshot name {snapshotId}");
        }

        public async Task<int> CleanupOldUpdatesAsync(string documentId, int maxUpdates = 100, TimeSpan? maxAge = null) {
            ValidateDocumentId(documentId);
            return await ExecuteWithLockAsync(async () => {
                var conn = GetOpenConnection();
                using var transaction = conn.BeginTransaction();

                // Use a CTE or subquery to identify IDs to delete (provider-specific; assumes SQL Server)
                string sql = @"
                    WITH ToDelete AS (
                        SELECT Id
                        FROM (
                            SELECT Id, ROW_NUMBER() OVER (ORDER BY Timestamp DESC) AS RowNum
                            FROM Updates
                            WHERE DocumentId = @DocumentId
                        ) Ranked
                        WHERE RowNum > @MaxUpdates
                    )
                    DELETE FROM Updates
                    WHERE Id IN (SELECT Id FROM ToDelete)";

                if (maxAge.HasValue) {
                    var cutoff = DateTime.UtcNow - maxAge.Value;
                    sql = @"
                        WITH ToDelete AS (
                            SELECT Id
                            FROM Updates
                            WHERE DocumentId = @DocumentId AND Timestamp < @Cutoff
                        )
                        DELETE FROM Updates
                        WHERE Id IN (SELECT Id FROM ToDelete)";
                    
                    if (maxUpdates > 0) {
                        sql = @"
                            WITH Ranked AS (
                                SELECT Id, ROW_NUMBER() OVER (ORDER BY Timestamp DESC) AS RowNum
                                FROM Updates
                                WHERE DocumentId = @DocumentId
                            ),
                            ToDelete AS (
                                SELECT Id
                                FROM Ranked
                                WHERE RowNum > @MaxUpdates OR Timestamp < @Cutoff
                            )
                            DELETE FROM Updates
                            WHERE Id IN (SELECT Id FROM ToDelete)";
                    }
                }

                using var cmd = conn.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = sql;
                AddParameter(cmd, "@DocumentId", documentId);
                AddParameter(cmd, "@MaxUpdates", maxUpdates);
                if (maxAge.HasValue) AddParameter(cmd, "@Cutoff", DateTime.UtcNow - maxAge.Value);

                var deletedCount = await cmd.ExecuteNonQueryAsync();
                if (deletedCount > 0) {
                    transaction.Commit();
                    _logger.LogInformation("Cleaned up {Count} old updates for document {DocumentId}", deletedCount, documentId);
                }
                return deletedCount;
            }, $"Cleaned up old updates for document {documentId}");
        }

        public async Task<int> CleanupAllDocumentsAsync(int maxUpdatesPerDocument = 100, TimeSpan? maxAge = null) {
            return await ExecuteWithLockAsync(async () => {
                var conn = GetOpenConnection();
                var documentIds = await GetAllDocumentIdsAsync(conn);

                var totalDeleted = 0;
                foreach (var docId in documentIds) {
                    totalDeleted += await CleanupOldUpdatesAsync(docId, maxUpdatesPerDocument, maxAge);
                }

                _logger.LogInformation("Cleaned up {TotalDeleted} updates across {DocumentCount} documents", totalDeleted, documentIds.Count);
                return totalDeleted;
            }, "Cleaned up all documents");
        }

        public async Task<List<DBDocumentUpdate>> CreateUpdatesAsync(IEnumerable<(string documentId, string type, byte[] update)> updates) {
            if (updates == null) throw new ArgumentNullException(nameof(updates));

            var dbUpdates = new List<DBDocumentUpdate>();
            var timestamp = DateTime.UtcNow;
            var clientId = Guid.NewGuid();

            foreach (var (documentId, type, updateData) in updates) {
                if (string.IsNullOrEmpty(documentId) || string.IsNullOrEmpty(type) || updateData == null) continue;

                dbUpdates.Add(new DBDocumentUpdate {
                    Id = Guid.NewGuid(),
                    DocumentId = documentId,
                    Type = type,
                    Data = updateData,
                    Timestamp = timestamp,
                    ClientId = clientId
                });
            }

            if (dbUpdates.Any()) {
                await ExecuteWithLockAsync(async () => {
                    var conn = GetOpenConnection();
                    using var transaction = conn.BeginTransaction();
                    const int batchSize = 1000;
                    for (int i = 0; i < dbUpdates.Count; i += batchSize) {
                        var batch = dbUpdates.Skip(i).Take(batchSize);
                        foreach (var upd in batch) {
                            using var cmd = conn.CreateCommand();
                            cmd.Transaction = transaction;
                            cmd.CommandText = "INSERT INTO Updates (Id, DocumentId, Type, Data, Timestamp, ClientId) VALUES (@Id, @DocumentId, @Type, @Data, @Timestamp, @ClientId)";
                            AddParameter(cmd, "@Id", upd.Id);
                            AddParameter(cmd, "@DocumentId", upd.DocumentId);
                            AddParameter(cmd, "@Type", upd.Type);
                            AddParameter(cmd, "@Data", upd.Data);
                            AddParameter(cmd, "@Timestamp", upd.Timestamp);
                            AddParameter(cmd, "@ClientId", upd.ClientId);
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                    transaction.Commit();
                    _logger.LogInformation("Created batch of {Count} updates", dbUpdates.Count);
                }, $"Created batch of {dbUpdates.Count} updates");
            }

            return dbUpdates;
        }

        public void Dispose() {
            if (_disposed) return;
            _context?.Dispose();
            _contextLock.Dispose();
            _disposed = true;
        }

        private DbConnection GetOpenConnection() {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) conn.Open();
            return conn;
        }

        private void AddParameter(IDbCommand cmd, string name, object value) {
            var param = cmd.CreateParameter();
            param.ParameterName = name;
            param.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(param);
        }

        private DBDocument MapDocument(DbDataReader reader) {
            return new DBDocument {
                Id = reader.GetString("Id"),
                Type = reader.GetString("Type"),
                Data = (byte[])reader["Data"],
                LastModified = reader.GetDateTime("LastModified")
            };
        }

        private DBDocumentUpdate MapUpdate(DbDataReader reader) {
            return new DBDocumentUpdate {
                Id = reader.GetGuid("Id"),
                DocumentId = reader.GetString("DocumentId"),
                Type = reader.GetString("Type"),
                Data = (byte[])reader["Data"],
                Timestamp = reader.GetDateTime("Timestamp"),
                ClientId = reader.GetGuid("ClientId")
            };
        }

        private DBSnapshot MapSnapshot(DbDataReader reader) {
            return new DBSnapshot {
                Id = reader.GetGuid("Id"),
                DocumentId = reader.GetString("DocumentId"),
                Name = reader.IsDBNull("Name") ? null : reader.GetString("Name"),
                Data = (byte[])reader["Data"],
                Timestamp = reader.GetDateTime("Timestamp")
            };
        }

        private async Task<string> GetExistingTypeAsync(DbConnection conn, DbTransaction transaction, string documentId) {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "SELECT Type FROM Documents WHERE Id = @Id";
            AddParameter(cmd, "@Id", documentId);
            var result = await cmd.ExecuteScalarAsync();
            return result as string;
        }

        // Helper for CleanupAll
        private async Task<List<string>> GetAllDocumentIdsAsync(DbConnection conn) {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id FROM Documents";
            using var reader = await cmd.ExecuteReaderAsync();
            var ids = new List<string>();
            while (await reader.ReadAsync()) {
                ids.Add(reader.GetString("Id"));
            }
            return ids;
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