using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Repositories {
    /// <summary>
    /// Defines the contract for a project repository, handling document and event persistence.
    /// </summary>
    public interface IProjectRepository : IDisposable {
        /// <summary>Initializes the database schema.</summary>
        /// <param name="ct">The cancellation token.</param>
        Task InitializeDatabaseAsync(CancellationToken ct);

        /// <summary>Creates a new database transaction.</summary>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task containing the transaction.</returns>
        Task<ITransaction> CreateTransactionAsync(CancellationToken ct);

        /// <summary>Retrieves a document's serialized data by its ID.</summary>
        /// <typeparam name="T">The type of the document.</typeparam>
        /// <param name="id">The document ID.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task containing the result with the document's byte array.</returns>
        Task<Result<byte[]>> GetDocumentBlobAsync<T>(string id, CancellationToken ct) where T : BaseDocument;

        /// <summary>Inserts a new command event into the repository.</summary>
        /// <param name="evt">The command event.</param>
        /// <param name="tx">The transaction (optional).</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task representing the result of the operation.</returns>
        Task<Result<Unit>> InsertEventAsync(BaseCommand evt, ITransaction? tx, CancellationToken ct);

        /// <summary>Inserts a new document into the repository.</summary>
        /// <param name="id">The document ID.</param>
        /// <param name="type">The document type string.</param>
        /// <param name="data">The serialized document data.</param>
        /// <param name="version">The document version.</param>
        /// <param name="tx">The transaction (optional).</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task representing the result of the operation.</returns>
        Task<Result<Unit>> InsertDocumentAsync(string id, string type, byte[] data, ulong version, ITransaction? tx,
            CancellationToken ct);

        /// <summary>Updates an existing document in the repository.</summary>
        /// <param name="id">The document ID.</param>
        /// <param name="data">The new serialized document data.</param>
        /// <param name="version">The new document version.</param>
        /// <param name="tx">The transaction (optional).</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task representing the result of the operation.</returns>
        Task<Result<Unit>> UpdateDocumentAsync(string id, byte[] data, ulong version, ITransaction? tx,
            CancellationToken ct);

        /// <summary>Retrieves a user-specific value by key.</summary>
        /// <param name="key">The key.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task containing the result with the value string.</returns>
        Task<Result<string>> GetUserValueAsync(string key, CancellationToken ct);

        /// <summary>Updates or inserts a user-specific value.</summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value string.</param>
        /// <param name="tx">The transaction (optional).</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task representing the result of the operation.</returns>
        Task<Result<Unit>> UpsertUserValueAsync(string key, string value, ITransaction? tx, CancellationToken ct);

        /// <summary>Retrieves all events that haven't been synced with the server.</summary>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task containing a list of unsynced events.</returns>
        Task<IReadOnlyList<BaseCommand>> GetUnsyncedEventsAsync(CancellationToken ct);

        /// <summary>Updates the server timestamp for a specific event.</summary>
        /// <param name="eventId">The event ID.</param>
        /// <param name="serverTimestamp">The server timestamp.</param>
        /// <param name="tx">The transaction (optional).</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task representing the result of the operation.</returns>
        Task<Result<Unit>> UpdateEventServerTimestampAsync(string eventId, ulong serverTimestamp, ITransaction? tx,
            CancellationToken ct);
    }
}