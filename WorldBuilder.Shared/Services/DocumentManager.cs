using DatReaderWriter.Lib;
using MemoryPack;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Data.Common;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Repositories;

namespace WorldBuilder.Shared.Services;

public class DocumentManager : IDocumentManager, IDisposable {
    private readonly IProjectRepository _repo;
    private readonly IDatReaderWriter _dats;
    private readonly ILogger<DocumentManager> _logger;
    private readonly ConcurrentDictionary<string, DocumentCacheEntry> _cache = new();
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private readonly Timer _cleanupTimer;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _unusedTimeout = TimeSpan.FromSeconds(60);
    private bool _disposed;

    /// <summary>
    /// The user id of the current local user
    /// </summary>
    public string UserId { get; private set; } = new Guid().ToString();

    public DocumentManager(IProjectRepository repo, IDatReaderWriter dats, ILogger<DocumentManager> logger) {
        _repo = repo;
        _dats = dats;
        _logger = logger;
        _cleanupTimer = new Timer(CleanupCallback, null, _cleanupInterval, _cleanupInterval);
    }

    public async Task InitializeAsync(CancellationToken ct) {
        _logger.LogInformation("Initializing DocumentManager");
        await _repo.InitializeDatabaseAsync(ct);
        var userIdResult = await GetUserValueAsync("UserId", UserId, ct);
        if (userIdResult.IsSuccess) {
            UserId = userIdResult.Value;
        }

        _logger.LogInformation("DocumentManager initialized with UserId: {UserId}", UserId);
    }

    public async Task<ITransaction> CreateTransactionAsync(CancellationToken ct) {
        return await _repo.CreateTransactionAsync(ct);
    }

    public async Task<Result<string>> GetUserValueAsync(string key, string defaultValue, CancellationToken ct) {
        var valueResult = await _repo.GetUserValueAsync(key, ct);
        if (valueResult.IsFailure) {
            return Result<string>.Failure(valueResult.Error);
        }

        var value = valueResult.Value;
        if (value is null) {
            await using var tx = await _repo.CreateTransactionAsync(ct);
            var upsertResult = await _repo.UpsertUserValueAsync(key, defaultValue, tx, ct);
            if (upsertResult.IsFailure) {
                return Result<string>.Failure(upsertResult.Error);
            }

            await tx.CommitAsync(ct);
            return Result<string>.Success(defaultValue);
        }

        return Result<string>.Success(value);
    }

    public async Task<Result<DocumentRental<T>>> CreateDocumentAsync<T>(T document, ITransaction tx,
        CancellationToken ct) where T : BaseDocument {
        if (_disposed) {
            return Result<DocumentRental<T>>.Failure("DocumentManager is disposed", "OBJECT_DISPOSED");
        }

        if (document == null) {
            return Result<DocumentRental<T>>.Failure("Document cannot be null", "ARGUMENT_NULL");
        }

        _logger.LogDebug("Creating document with ID: {DocumentId}, Type: {DocumentType}", document.Id, typeof(T).Name);

        await _cacheLock.WaitAsync(ct);
        try {
            if (_cache.ContainsKey(document.Id)) {
                _logger.LogWarning("Document with ID '{DocumentId}' already exists in cache", document.Id);
                return Result<DocumentRental<T>>.Failure($"Document with ID '{document.Id}' already exists in cache",
                    "DOCUMENT_EXISTS");
            }

            // Persist to database
            var blob = document.Serialize();
            var insertResult =
                await _repo.InsertDocumentAsync(document.Id, typeof(T).Name, blob, document.Version, tx, ct);
            if (insertResult.IsFailure) {
                return Result<DocumentRental<T>>.Failure(insertResult.Error);
            }

            _logger.LogDebug("Document with ID {DocumentId} inserted into database", document.Id);

            // Add to cache
            var entry = new DocumentCacheEntry(document);
            entry.IncrementRentCount();
            _cache[document.Id] = entry;
            _logger.LogDebug("Document with ID {DocumentId} added to cache", document.Id);

            return Result<DocumentRental<T>>.Success(new DocumentRental<T>(document,
                () => ReturnDocument(document.Id)));
        }
        finally {
            _cacheLock.Release();
        }
    }

    public async Task<Result<DocumentRental<T>>> RentDocumentAsync<T>(string id, CancellationToken ct)
        where T : BaseDocument {
        if (_disposed) {
            return Result<DocumentRental<T>>.Failure("DocumentManager is disposed", "OBJECT_DISPOSED");
        }

        _logger.LogDebug("Renting document with ID: {DocumentId}", id);

        await _cacheLock.WaitAsync(ct);
        try {
            if (_cache.TryGetValue(id, out var entry)) {
                var doc = (T)entry.Document;
                entry.IncrementRentCount();
                _logger.LogDebug("Document with ID {DocumentId} found in cache", id);
                return Result<DocumentRental<T>>.Success(new DocumentRental<T>(doc, () => ReturnDocument(id)));
            }

            _logger.LogDebug("Document with ID {DocumentId} not found in cache, loading from database", id);
            var newDoc = await LoadDocumentAsync<T>(id, ct);
            if (newDoc == null) {
                _logger.LogWarning("Document with ID {DocumentId} not found in database", id);
                return Result<DocumentRental<T>>.Failure($"Document with ID {id} not found in database",
                    "DOCUMENT_NOT_FOUND");
            }

            var newEntry = new DocumentCacheEntry(newDoc);
            newEntry.IncrementRentCount();
            _cache[id] = newEntry;
            _logger.LogDebug("Document with ID {DocumentId} loaded from database and added to cache", id);

            return Result<DocumentRental<T>>.Success(new DocumentRental<T>(newDoc, () => ReturnDocument(id)));
        }
        finally {
            _cacheLock.Release();
        }
    }

    private void ReturnDocument(string id) {
        if (_disposed) return;

        _cacheLock.Wait();
        try {
            if (_cache.TryGetValue(id, out var entry)) {
                entry.DecrementRentCount();
            }
        }
        finally {
            _cacheLock.Release();
        }
    }

    public async Task<Result<Unit>> PersistDocumentAsync<T>(DocumentRental<T> rental, ITransaction tx,
        CancellationToken ct) where T : BaseDocument {
        if (_disposed) {
            return Result<Unit>.Failure("DocumentManager is disposed", "OBJECT_DISPOSED");
        }

        if (rental == null) {
            return Result<Unit>.Failure("Rental cannot be null", "ARGUMENT_NULL");
        }

        var doc = rental.Document;
        _logger.LogDebug("Persisting document with ID: {DocumentId}, Version: {Version}", doc.Id, doc.Version);
        var blob = doc.Serialize();
        var updateResult = await _repo.UpdateDocumentAsync(doc.Id, blob, doc.Version, tx, ct);
        if (updateResult.IsFailure) {
            return Result<Unit>.Failure(updateResult.Error);
        }

        _logger.LogDebug("Document with ID {DocumentId} persisted to database", doc.Id);
        return Result<Unit>.Success(Unit.Value);
    }

    public async Task<Result<bool>> ApplyLocalEventAsync(BaseCommand evt, ITransaction tx, CancellationToken ct) {
        if (_disposed) {
            return Result<bool>.Failure("DocumentManager is disposed", "OBJECT_DISPOSED");
        }

        if (evt == null) {
            return Result<bool>.Failure("Event cannot be null", "ARGUMENT_NULL");
        }

        _logger.LogDebug("Applying event {EventId} of type {EventType} for user {UserId}", evt.Id, evt.GetType().Name,
            UserId);

        evt.UserId = UserId;

        try {
            var res = await evt.ApplyAsync(this, _dats, tx, ct);
            if (res.IsFailure) {
                return Result<bool>.Failure(res.Error);
            }

            var insertEventResult = await _repo.InsertEventAsync(evt, tx, ct);
            if (insertEventResult.IsFailure) {
                return Result<bool>.Failure(insertEventResult.Error);
            }

            _logger.LogDebug("Event {EventId} applied successfully with result: {Result}", evt.Id, res.Value);

            return res;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error applying event {EventId} of type {EventType}", evt.Id, evt.GetType().Name);
            return Result<bool>.Failure($"Error applying event: {ex.Message}", "EVENT_APPLICATION_ERROR");
        }
    }

    public async Task<Result<TResult>> ApplyLocalEventAsync<TResult>(BaseCommand<TResult> evt, ITransaction tx,
        CancellationToken ct) {
        if (_disposed) {
            return Result<TResult>.Failure("DocumentManager is disposed", "OBJECT_DISPOSED");
        }

        if (evt == null) {
            return Result<TResult>.Failure("Event cannot be null", "ARGUMENT_NULL");
        }

        _logger.LogDebug("Applying event {EventId} of type {EventType} for user {UserId}", evt.Id, evt.GetType().Name,
            UserId);

        evt.UserId = UserId;

        try {
            var res = await evt.ApplyResultAsync(this, _dats, tx, ct);
            if (res.IsFailure) {
                return Result<TResult>.Failure(res.Error);
            }

            var insertEventResult = await _repo.InsertEventAsync(evt, tx, ct);
            if (insertEventResult.IsFailure) {
                return Result<TResult>.Failure(insertEventResult.Error);
            }

            _logger.LogDebug("Event {EventId} applied successfully", evt.Id);

            return res;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error applying event {EventId} of type {EventType}", evt.Id, evt.GetType().Name);
            return Result<TResult>.Failure($"Error applying event: {ex.Message}", "EVENT_APPLICATION_ERROR");
        }
    }

    private async Task<T?> LoadDocumentAsync<T>(string id, CancellationToken ct) where T : BaseDocument {
        _logger.LogDebug("Loading document with ID: {DocumentId} from database", id);
        var blobResult = await _repo.GetDocumentBlobAsync<T>(id, ct);
        if (blobResult.IsFailure || blobResult.Value == null) {
            _logger.LogWarning("Document with ID {DocumentId} not found in database", id);
            return null;
        }

        _logger.LogDebug("Document with ID {DocumentId} loaded from database", id);
        return BaseDocument.Deserialize<T>(blobResult.Value);
    }

    private void CleanupCallback(object? state) {
        if (_disposed) return;

        _logger.LogDebug("Starting cache cleanup");

        _cacheLock.Wait();
        try {
            var now = DateTime.UtcNow;
            var toRemove = new List<string>();

            foreach (var kvp in _cache) {
                var entry = kvp.Value;

                // Remove if not rented and past timeout
                if (entry.RentCount == 0 && now - entry.LastAccessTime > _unusedTimeout) {
                    _logger.LogDebug("Marking document {DocumentId} for removal due to timeout", kvp.Key);
                    toRemove.Add(kvp.Key);
                }
                // Clean up weak references that have been collected
                else if (!entry.IsAlive) {
                    _logger.LogDebug("Marking document {DocumentId} for removal due to garbage collection", kvp.Key);
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var key in toRemove) {
                _cache.TryRemove(key, out _);
                _logger.LogDebug("Removed document {DocumentId} from cache", key);
            }

            if (toRemove.Count > 0) {
                _logger.LogDebug("Cache cleanup completed, {RemovedCount} documents removed", toRemove.Count);
            }
            else {
                _logger.LogDebug("Cache cleanup completed, no documents removed");
            }
        }
        finally {
            _cacheLock.Release();
        }
    }

    public void Dispose() {
        if (_disposed) return;

        _logger.LogInformation("Disposing DocumentManager");

        _disposed = true;
        _cleanupTimer?.Dispose();
        _cacheLock?.Dispose();
        _cache.Clear();
    }

    private class DocumentCacheEntry {
        private int _rentCount;
        private readonly WeakReference<BaseDocument> _weakRef;
        private BaseDocument? _strongRef;
        private bool _isStale;

        public DocumentCacheEntry(BaseDocument document) {
            _strongRef = document;
            _weakRef = new WeakReference<BaseDocument>(document);
            LastAccessTime = DateTime.UtcNow;
        }

        public BaseDocument Document {
            get {
                LastAccessTime = DateTime.UtcNow;
                if (_strongRef != null) return _strongRef;

                if (_weakRef.TryGetTarget(out var doc)) {
                    return doc;
                }

                throw new ObjectDisposedException("Document has been garbage collected");
            }
        }

        public int RentCount => Volatile.Read(ref _rentCount);
        public DateTime LastAccessTime { get; private set; }
        public bool IsStale => _isStale;

        public bool IsAlive => _strongRef != null || _weakRef.TryGetTarget(out _);

        public void IncrementRentCount() {
            Interlocked.Increment(ref _rentCount);
            LastAccessTime = DateTime.UtcNow;
        }

        public void DecrementRentCount() {
            var newCount = Interlocked.Decrement(ref _rentCount);
            LastAccessTime = DateTime.UtcNow;

            // Release strong reference when no longer rented
            if (newCount == 0) {
                _strongRef?.Dispose();
                _strongRef = null;
            }
        }

        public void MarkStale() {
            _isStale = true;
        }
    }

    public class DocumentRental<T> : IDisposable where T : BaseDocument {
        private readonly T _document;
        private readonly Action _onReturn;
        private int _disposed;

        public DocumentRental(T document, Action onReturn) {
            _document = document;
            _onReturn = onReturn;
        }

        public T Document => _document;

        // Implicit conversion for convenience
        public static implicit operator T(DocumentRental<T> rental) => rental._document;

        public void Dispose() {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0) {
                _onReturn();
            }
        }
    }
}