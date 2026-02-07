using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Repositories {
    public interface IProjectRepository : IDisposable {
        Task InitializeDatabaseAsync(CancellationToken ct);
        Task<ITransaction> CreateTransactionAsync(CancellationToken ct);
        Task<Result<byte[]>> GetDocumentBlobAsync<T>(string id, CancellationToken ct) where T : BaseDocument;
        Task<Result<Unit>> InsertEventAsync(BaseCommand evt, ITransaction? tx, CancellationToken ct);

        Task<Result<Unit>> InsertDocumentAsync(string id, string type, byte[] data, ulong version, ITransaction? tx,
            CancellationToken ct);

        Task<Result<Unit>> UpdateDocumentAsync(string id, byte[] data, ulong version, ITransaction? tx,
            CancellationToken ct);

        Task<Result<string>> GetUserValueAsync(string key, CancellationToken ct);
        Task<Result<Unit>> UpsertUserValueAsync(string key, string value, ITransaction? tx, CancellationToken ct);

        // Sync-related methods
        Task<IReadOnlyList<BaseCommand>> GetUnsyncedEventsAsync(CancellationToken ct);

        Task<Result<Unit>> UpdateEventServerTimestampAsync(string eventId, ulong serverTimestamp, ITransaction? tx,
            CancellationToken ct);
    }
}