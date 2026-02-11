using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace WorldBuilder.Shared.Lib {
    /// <summary>
    /// Adapts a DbTransaction to the ITransaction interface for WorldBuilder operations.
    /// </summary>
    public class DatabaseTransactionAdapter : ITransaction {
        private readonly DbTransaction _transaction;

        /// <summary>
        /// Initializes a new instance of the DatabaseTransactionAdapter class.
        /// </summary>
        /// <param name="transaction">The database transaction to wrap.</param>
        public DatabaseTransactionAdapter(DbTransaction transaction) {
            _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        }

        /// <summary>
        /// Gets the underlying database transaction.
        /// </summary>
        public DbTransaction UnderlyingTransaction => _transaction;

        /// <summary>
        /// Commits the underlying database transaction asynchronously.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous commit operation.</returns>
        public async Task CommitAsync(CancellationToken cancellationToken = default) {
            await _transaction.CommitAsync(cancellationToken);
        }

        /// <summary>
        /// Rolls back the underlying database transaction asynchronously.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous rollback operation.</returns>
        public async Task RollbackAsync(CancellationToken cancellationToken = default) {
            await _transaction.RollbackAsync(cancellationToken);
        }

        /// <summary>
        /// Disposes the underlying database transaction and releases associated resources.
        /// </summary>
        public void Dispose() {
            _transaction?.Dispose();
        }

        /// <summary>
        /// Disposes the underlying database transaction asynchronously and releases associated resources.
        /// </summary>
        public async ValueTask DisposeAsync() {
            if (_transaction != null) {
                await _transaction.DisposeAsync();
            }
        }
    }
}