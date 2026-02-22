using FluentMigrator.Runner;
using MemoryPack;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Migrations;
using WorldBuilder.Shared.Models;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace WorldBuilder.Shared.Repositories {
    /// <summary>
    /// A SQLite-based implementation of <see cref="IProjectRepository"/>.
    /// </summary>
    public class SQLiteProjectRepository : IProjectRepository {
        /// <summary>The underlying SQLite connection.</summary>
        public readonly SqliteConnection Connection;
        private readonly ILogger<SQLiteProjectRepository>? _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="SQLiteProjectRepository"/> class.
        /// </summary>
        /// <param name="connectionString">The SQLite connection string.</param>
        /// <param name="logger">The logger (optional).</param>
        public SQLiteProjectRepository(string connectionString, ILogger<SQLiteProjectRepository>? logger = null) {
            Connection = new SqliteConnection(connectionString);
            Connection.Open();

            // Enable WAL mode for better performance and concurrency
            using (var cmd = Connection.CreateCommand()) {
                cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
                cmd.ExecuteNonQuery();
            }

            _logger = logger ?? NullLogger<SQLiteProjectRepository>.Instance;
        }

        /// <inheritdoc/>
        public Task InitializeDatabaseAsync(CancellationToken ct) {
            _logger?.LogInformation("Initializing database");
            SQLitePCL.Batteries_V2.Init();

            var serviceProvider = CreateServices();
            using var scope = serviceProvider.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
            runner.MigrateUp();
            _logger?.LogInformation("Database initialized successfully");
            return Task.CompletedTask;
        }

        private IServiceProvider CreateServices() {
            return new ServiceCollection()
                .AddFluentMigratorCore()
                .ConfigureRunner(rb => rb
                    .AddSQLite()
                    .WithGlobalConnectionString(Connection.ConnectionString)
                    .ScanIn(typeof(Migration_001_InitialSchema).Assembly).For.Migrations())
                .BuildServiceProvider(false);
        }

        /// <inheritdoc/>
        public async Task<ITransaction> CreateTransactionAsync(CancellationToken ct) {
            var dbTransaction = await Connection.BeginTransactionAsync(ct);
            return new DatabaseTransactionAdapter(dbTransaction);
        }

        private static Result<SqliteTransaction?> GetDbTransaction(ITransaction? transaction) {
            if (transaction == null) {
                return Result<SqliteTransaction?>.Success(null);
            }

            if (transaction is DatabaseTransactionAdapter adapter) {
                var sqliteTransaction = adapter.UnderlyingTransaction as SqliteTransaction;
                if (sqliteTransaction == null) {
                    return Result<SqliteTransaction?>.Failure(
                        $"Transaction does not contain a valid SqliteTransaction. Type: {adapter.UnderlyingTransaction?.GetType()}",
                        "TRANSACTION_ERROR");
                }

                return Result<SqliteTransaction?>.Success(sqliteTransaction);
            }

            return Result<SqliteTransaction?>.Failure($"Transaction type {transaction.GetType().Name} is not supported",
                "TRANSACTION_ERROR");
        }

        /// <inheritdoc/>
        public async Task<Result<string>> GetUserValueAsync(string key, CancellationToken ct) {
            try {
                _logger?.LogDebug("Retrieving user value for key: {Key}", key);
                var sql = "SELECT Value FROM UserKeyValues WHERE Key = @key";
                await using var cmd = Connection.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@key", key);
                var obj = await cmd.ExecuteScalarAsync(ct);
                if (obj == null) {
                    _logger?.LogDebug("User value for key {Key} not found", key);
                    return Result<string>.Failure($"User value for key {key} not found", "USER_VALUE_NOT_FOUND");
                }
                else {
                    _logger?.LogDebug("User value for key {Key} retrieved successfully", key);
                    return Result<string>.Success((string)obj!);
                }
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error retrieving user value for key: {Key}", key);
                return Result<string>.Failure($"Error retrieving user value: {ex.Message}", "DATABASE_ERROR");
            }
        }

        /// <inheritdoc/>
        public async Task<Result<Unit>> UpsertUserValueAsync(string key, string value, ITransaction? tx,
            CancellationToken ct) {
            try {
                _logger?.LogDebug("Upserting user value for key: {Key}", key);
                var dbTxResult = GetDbTransaction(tx);
                if (dbTxResult.IsFailure) {
                    return Result<Unit>.Failure(dbTxResult.Error);
                }

                var dbTx = dbTxResult.Value;
                const string sql = @"
        INSERT INTO UserKeyValues (Key, Value)
        VALUES (@key, @value)
        ON CONFLICT(Key) DO UPDATE SET Value = @value";

                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@key", key);
                cmd.Parameters.AddWithValue("@value", value);
                await cmd.ExecuteNonQueryAsync(ct);
                _logger?.LogDebug("User value for key {Key} upserted successfully", key);
                return Result<Unit>.Success(Unit.Value);
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error upserting user value for key: {Key}", key);
                return Result<Unit>.Failure($"Error upserting user value: {ex.Message}", "DATABASE_ERROR");
            }
        }

        /// <inheritdoc/>
        public async Task<Result<Unit>> InsertEventAsync(BaseCommand evt, ITransaction? tx, CancellationToken ct) {
            try {
                _logger?.LogDebug("Inserting event {EventId} of type {EventType} for user {UserId}", evt.Id,
                    evt.GetType().Name, evt.UserId);
                var dbTxResult = GetDbTransaction(tx);
                if (dbTxResult.IsFailure) {
                    return Result<Unit>.Failure(dbTxResult.Error);
                }

                var dbTx = dbTxResult.Value;
                if (string.IsNullOrEmpty(evt.Id)) {
                    return Result<Unit>.Failure("Event Id cannot be null or empty", "ARGUMENT_ERROR");
                }

                if (string.IsNullOrEmpty(evt.UserId)) {
                    return Result<Unit>.Failure("UserId cannot be null or empty", "ARGUMENT_ERROR");
                }

                const string sql = @"
                    INSERT INTO Events (Id, Type, Data, UserId)
                    VALUES (@id, @type, @data, @uid)";

                var data = evt.Serialize();
                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", evt.Id);
                cmd.Parameters.AddWithValue("@type", evt.GetType().Name);
                cmd.Parameters.AddWithValue("@data", data);
                cmd.Parameters.AddWithValue("@uid", evt.UserId);
                await cmd.ExecuteNonQueryAsync(ct);
                _logger?.LogDebug("Event {EventId} inserted successfully", evt.Id);
                return Result<Unit>.Success(Unit.Value);
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error inserting event {EventId} of type {EventType}", evt.Id,
                    evt.GetType().Name);
                return Result<Unit>.Failure($"Error inserting event: {ex.Message}", "DATABASE_ERROR");
            }
        }

        /// <inheritdoc/>
        public async Task<Result<Unit>> InsertDocumentAsync(string id, string type, byte[] data, ulong version,
            ITransaction? tx, CancellationToken ct) {
            try {
                _logger?.LogDebug("Inserting document with ID: {DocumentId}, Type: {DocumentType}, Version: {Version}",
                    id, type, version);
                var dbTxResult = GetDbTransaction(tx);
                if (dbTxResult.IsFailure) {
                    return Result<Unit>.Failure(dbTxResult.Error);
                }

                var dbTx = dbTxResult.Value;
                const string sql = @"
                    INSERT INTO Documents (Id, Type, Data, Version, LastModified)
                    VALUES (@id, @type, @data, @ver, CURRENT_TIMESTAMP)";

                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@type", type);
                cmd.Parameters.AddWithValue("@data", data);
                cmd.Parameters.AddWithValue("@ver", (long)version);
                await cmd.ExecuteNonQueryAsync(ct);
                _logger?.LogDebug("Document with ID {DocumentId} inserted successfully", id);
                return Result<Unit>.Success(Unit.Value);
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error inserting document with ID: {DocumentId}", id);
                return Result<Unit>.Failure($"Error inserting document: {ex.Message}", "DATABASE_ERROR");
            }
        }

        /// <inheritdoc/>
        public async Task<Result<Unit>> UpdateDocumentAsync(string id, byte[] data, ulong version, ITransaction? tx,
            CancellationToken ct) {
            try {
                _logger?.LogDebug("Updating document with ID: {DocumentId}, Version: {Version}", id, version);
                var dbTxResult = GetDbTransaction(tx);
                if (dbTxResult.IsFailure) {
                    return Result<Unit>.Failure(dbTxResult.Error);
                }

                var dbTx = dbTxResult.Value;
                const string sql = @"
                    UPDATE Documents
                    SET Data = @data,
                        Version = @ver,
                        LastModified = CURRENT_TIMESTAMP
                    WHERE Id = @id AND Version <= @ver";

                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@data", data);
                cmd.Parameters.AddWithValue("@ver", (long)version);
                int rows = await cmd.ExecuteNonQueryAsync(ct);
                if (rows == 0) {
                    _logger?.LogWarning("UpdateDocumentAsync: Document with ID {DocumentId} not found to update", id);
                    return Result<Unit>.Failure($"Document with ID {id} not found to update", "DOCUMENT_NOT_FOUND");
                }
                _logger?.LogDebug("Document with ID {DocumentId} updated successfully", id);
                return Result<Unit>.Success(Unit.Value);
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error updating document with ID: {DocumentId}", id);
                return Result<Unit>.Failure($"Error updating document: {ex.Message}", "DATABASE_ERROR");
            }
        }

        /// <inheritdoc/>
        public async Task<Result<byte[]>> GetDocumentBlobAsync<T>(string id, CancellationToken ct)
            where T : BaseDocument {
            try {
                _logger?.LogDebug("Retrieving document blob with ID: {DocumentId}", id);
                var sql = "SELECT Data FROM Documents WHERE Id = @id";
                await using var cmd = Connection.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", id);
                var obj = await cmd.ExecuteScalarAsync(ct);
                if (obj == null) {
                    _logger?.LogWarning("Document with ID {DocumentId} not found in database", id);
                    return Result<byte[]>.Failure($"Document with ID {id} not found in database", "DOCUMENT_NOT_FOUND");
                }
                else {
                    _logger?.LogDebug("Document blob with ID {DocumentId} retrieved successfully", id);
                    return Result<byte[]>.Success((byte[])obj!);
                }
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error retrieving document blob with ID: {DocumentId}", id);
                return Result<byte[]>.Failure($"Error retrieving document: {ex.Message}", "DATABASE_ERROR");
            }
        }

        public async Task<Result<Unit>> DeleteDocumentAsync(string id, ITransaction? tx, CancellationToken ct) {
            try {
                _logger?.LogDebug("Deleting document with ID: {DocumentId}", id);
                var dbTxResult = GetDbTransaction(tx);
                if (dbTxResult.IsFailure) {
                    return Result<Unit>.Failure(dbTxResult.Error);
                }

                var dbTx = dbTxResult.Value;
                const string sql = @"DELETE FROM Documents WHERE Id = @id";

                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", id);
                await cmd.ExecuteNonQueryAsync(ct);
                _logger?.LogDebug("Document with ID {DocumentId} deleted successfully", id);
                return Result<Unit>.Success(Unit.Value);
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error deleting document with ID: {DocumentId}", id);
                return Result<Unit>.Failure($"Error deleting document: {ex.Message}", "DATABASE_ERROR");
            }
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<BaseCommand>> GetUnsyncedEventsAsync(CancellationToken ct) {
            var events = new List<BaseCommand>();
            try {
                _logger?.LogDebug("Retrieving unsynced events");
                const string sql = "SELECT Data FROM Events WHERE ServerTimestamp IS NULL ORDER BY Created ASC";
                await using var cmd = Connection.CreateCommand();
                cmd.CommandText = sql;
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) {
                    var data = (byte[])reader["Data"];
                    var evt = BaseCommand.Deserialize(data);
                    if (evt != null) {
                        events.Add(evt);
                    }
                }

                _logger?.LogDebug("Retrieved {Count} unsynced events", events.Count);
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error retrieving unsynced events");
            }

            return events;
        }

        /// <inheritdoc/>
        public async Task<Result<Unit>> UpdateEventServerTimestampAsync(string eventId, ulong serverTimestamp,
            ITransaction? tx, CancellationToken ct) {
            try {
                _logger?.LogDebug("Updating ServerTimestamp for event {EventId} to {Timestamp}", eventId,
                    serverTimestamp);
                var dbTxResult = GetDbTransaction(tx);
                if (dbTxResult.IsFailure) {
                    return Result<Unit>.Failure(dbTxResult.Error);
                }

                var dbTx = dbTxResult.Value;
                const string sql = "UPDATE Events SET ServerTimestamp = @ts WHERE Id = @id";

                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = dbTx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", eventId);
                cmd.Parameters.AddWithValue("@ts", (long)serverTimestamp);
                await cmd.ExecuteNonQueryAsync(ct);
                _logger?.LogDebug("ServerTimestamp updated for event {EventId}", eventId);
                return Result<Unit>.Success(Unit.Value);
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error updating ServerTimestamp for event {EventId}", eventId);
                return Result<Unit>.Failure($"Error updating event timestamp: {ex.Message}", "DATABASE_ERROR");
            }
        }

        /// <inheritdoc/>
        public void Dispose() {
            _logger?.LogInformation("Disposing SQLiteProjectRepository");
            Connection?.Close();
        }
    }
}