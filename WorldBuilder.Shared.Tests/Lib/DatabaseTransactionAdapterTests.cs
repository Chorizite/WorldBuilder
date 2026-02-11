using Microsoft.Data.Sqlite;
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using Xunit;

namespace WorldBuilder.Shared.Tests.Lib {
    public class DatabaseTransactionAdapterTests : IAsyncLifetime {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        private SqliteConnection _connection;
        private SqliteTransaction _dbTransaction;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

        public async Task InitializeAsync() {
            _connection = new SqliteConnection("Data Source=:memory:");
            await _connection.OpenAsync();
            _dbTransaction = (SqliteTransaction)await _connection.BeginTransactionAsync();
        }

        public async Task DisposeAsync() {
            await _dbTransaction.DisposeAsync();
            await _connection.DisposeAsync();
        }

        [Fact]
        public void Constructor_WithValidTransaction_Succeeds() {
            // Arrange & Act
            var adapter = new DatabaseTransactionAdapter(_dbTransaction);

            // Assert
            Assert.NotNull(adapter);
        }

        [Fact]
        public void Constructor_WithNullTransaction_ThrowsArgumentNullException() {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new DatabaseTransactionAdapter(null!));
        }

        [Fact]
        public void UnderlyingTransaction_ReturnsCorrectTransaction() {
            // Arrange
            var adapter = new DatabaseTransactionAdapter(_dbTransaction);

            // Act
            var underlyingTransaction = adapter.UnderlyingTransaction;

            // Assert
            Assert.Same(_dbTransaction, underlyingTransaction);
        }

        [Fact]
        public async Task CommitAsync_CommitsUnderlyingTransaction() {
            // Arrange
            var adapter = new DatabaseTransactionAdapter(_dbTransaction);

            // Create a test table and insert data within the transaction
            using var cmd = new SqliteCommand("CREATE TABLE test (id INTEGER)", _connection);
            cmd.Transaction = (SqliteTransaction)_dbTransaction;
            await cmd.ExecuteNonQueryAsync();

            // Act
            await adapter.CommitAsync();

            // Assert
            // Verify the transaction was committed by checking if the table exists in the database
            using var checkCmd = new SqliteCommand("SELECT name FROM sqlite_master WHERE type='table' AND name='test'", _connection);
            var result = await checkCmd.ExecuteScalarAsync();
            Assert.Equal("test", result);
        }

        [Fact]
        public async Task RollbackAsync_RollsBackUnderlyingTransaction() {
            // Arrange
            var adapter = new DatabaseTransactionAdapter(_dbTransaction);

            // Create a test table within the transaction
            using var cmd = new SqliteCommand("CREATE TABLE test (id INTEGER)", _connection);
            cmd.Transaction = (SqliteTransaction)_dbTransaction;
            await cmd.ExecuteNonQueryAsync();

            // Act
            await adapter.RollbackAsync();

            // Assert
            // Verify the transaction was rolled back by checking if the table doesn't exist
            using var checkCmd = new SqliteCommand("SELECT name FROM sqlite_master WHERE type='table' AND name='test'", _connection);
            var result = await checkCmd.ExecuteScalarAsync();
            Assert.Null(result);
        }

        [Fact]
        public async Task Dispose_DisposesUnderlyingTransaction() {
            // Create a separate connection for this test to avoid nested transaction issues
            using var testConnection = new SqliteConnection("Data Source=:memory:");
            await testConnection.OpenAsync();
            var dbTransaction = (SqliteTransaction)await testConnection.BeginTransactionAsync();
            var adapter = new DatabaseTransactionAdapter(dbTransaction);

            // Act
            adapter.Dispose();

            // Assert
            // Check that the transaction is no longer usable by attempting to commit again
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => dbTransaction.CommitAsync());
        }

        [Fact]
        public async Task DisposeAsync_DisposesUnderlyingTransaction() {
            // Create a separate connection for this test to avoid nested transaction issues
            using var testConnection = new SqliteConnection("Data Source=:memory:");
            await testConnection.OpenAsync();
            var dbTransaction = (SqliteTransaction)await testConnection.BeginTransactionAsync();
            var adapter = new DatabaseTransactionAdapter(dbTransaction);

            // Act
            await adapter.DisposeAsync();

            // Assert
            // Check that the transaction is no longer usable by attempting to commit again
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => dbTransaction.CommitAsync());
        }

    }
}