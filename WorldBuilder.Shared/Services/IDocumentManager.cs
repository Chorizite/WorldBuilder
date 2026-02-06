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
    public interface IDocumentManager {
        Task InitializeAsync(CancellationToken ct);
        Task<ITransaction> CreateTransactionAsync(CancellationToken ct);
        Task<Result<string>> GetUserValueAsync(string key, string defaultValue, CancellationToken ct);
        Task<Result<DocumentRental<T>>> CreateDocumentAsync<T>(T document, ITransaction tx, CancellationToken ct) where T : BaseDocument;
        Task<Result<DocumentRental<T>>> RentDocumentAsync<T>(string id, CancellationToken ct) where T : BaseDocument;
        Task<Result<Unit>> PersistDocumentAsync<T>(DocumentRental<T> rental, ITransaction tx, CancellationToken ct) where T : BaseDocument;
        Task<Result<bool>> ApplyLocalEventAsync(BaseCommand evt, ITransaction tx, CancellationToken ct);
        Task<Result<TResult>> ApplyLocalEventAsync<TResult>(BaseCommand<TResult> evt, ITransaction tx, CancellationToken ct);
    }
}