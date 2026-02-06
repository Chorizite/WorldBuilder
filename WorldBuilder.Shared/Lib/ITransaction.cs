using System;
using System.Threading;
using System.Threading.Tasks;

namespace WorldBuilder.Shared.Lib {
    /// <summary>
    /// Represents a database transaction abstraction for WorldBuilder operations.
    /// </summary>
    public interface ITransaction : IAsyncDisposable {
        /// <summary>
        /// Commits the transaction asynchronously.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous commit operation.</returns>
        Task CommitAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Rolls back the transaction asynchronously.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous rollback operation.</returns>
        Task RollbackAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Disposes the transaction and releases associated resources.
        /// </summary>
        void Dispose();
    }
}