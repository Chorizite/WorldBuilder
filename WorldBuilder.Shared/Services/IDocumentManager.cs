using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using static WorldBuilder.Shared.Services.DocumentManager;

namespace WorldBuilder.Shared.Services {
    /// <summary>
    /// Defines the contract for a document manager, handles document lifecycle, transactions, and event application.
    /// </summary>
    public interface IDocumentManager {
        /// <summary>Initializes the document manager.</summary>
        /// <param name="ct">The cancellation token.</param>
        Task InitializeAsync(CancellationToken ct);

        /// <summary>Creates a new transaction.</summary>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task containing the transaction.</returns>
        Task<ITransaction> CreateTransactionAsync(CancellationToken ct);

        /// <summary>Retrieves a user-specific value, or a default value if not found.</summary>
        /// <param name="key">The key.</param>
        /// <param name="defaultValue">The default value.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task containing the result with the value string.</returns>
        Task<Result<string>> GetUserValueAsync(string key, string defaultValue, CancellationToken ct);

        /// <summary>Creates a new document and returns a rental for it.</summary>
        /// <typeparam name="T">The type of document.</typeparam>
        /// <param name="document">The document instance.</param>
        /// <param name="tx">The transaction.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task containing the document rental result.</returns>
        Task<Result<DocumentRental<T>>> CreateDocumentAsync<T>(T document, ITransaction tx, CancellationToken ct) where T : BaseDocument;

        /// <summary>Rents an existing document by its ID.</summary>
        /// <typeparam name="T">The type of document.</typeparam>
        /// <param name="id">The document ID.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task containing the document rental result.</returns>
        Task<Result<DocumentRental<T>>> RentDocumentAsync<T>(string id, CancellationToken ct) where T : BaseDocument;

        /// <summary>Persists changes made to a rented document.</summary>
        /// <typeparam name="T">The type of document.</typeparam>
        /// <param name="rental">The document rental.</param>
        /// <param name="tx">The transaction.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task representing the result of the operation.</returns>
        Task<Result<Unit>> PersistDocumentAsync<T>(DocumentRental<T> rental, ITransaction tx, CancellationToken ct) where T : BaseDocument;

        /// <summary>Applies a local command event to the document system.</summary>
        /// <param name="evt">The command event.</param>
        /// <param name="tx">The transaction.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task containing a boolean result (true if success).</returns>
        Task<Result<bool>> ApplyLocalEventAsync(BaseCommand evt, ITransaction tx, CancellationToken ct);

        /// <summary>Applies a local command event and returns a typed result.</summary>
        /// <typeparam name="TResult">The type of the result.</typeparam>
        /// <param name="evt">The command event.</param>
        /// <param name="tx">The transaction.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task containing the typed result.</returns>
        Task<Result<TResult>> ApplyLocalEventAsync<TResult>(BaseCommand<TResult> evt, ITransaction tx, CancellationToken ct);
    }
}