using DatReaderWriter.Lib;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Data.Common;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Repositories;

namespace WorldBuilder.Shared.Services;

public partial class DocumentManager : IDocumentManager, IDisposable {
    private readonly IProjectRepository _repo;
    private readonly IDatReaderWriter _dats;
    private readonly ILogger<DocumentManager> _logger;
    private readonly ConcurrentDictionary<string, DocumentCacheEntry> _cache = new();
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private readonly System.Threading.Timer _cleanupTimer;
    private readonly WorldBuilder.Shared.Modules.Landscape.Services.ILandscapeDataProvider _landscapeDataProvider;
    private readonly WorldBuilder.Shared.Modules.Landscape.Services.ILandscapeCacheService _landscapeCacheService;
    private bool _disposed;

    /// <summary>
    /// The user id of the current local user
    /// </summary>
    [MemoryPackIgnore]
    public string UserId { get; private set; } = new Guid().ToString();

    /// <inheritdoc/>
    [MemoryPackIgnore]
    public IProjectRepository ProjectRepository => _repo;

    /// <inheritdoc/>
    [MemoryPackIgnore]
    public WorldBuilder.Shared.Modules.Landscape.Services.ILandscapeDataProvider LandscapeDataProvider => _landscapeDataProvider;

    /// <inheritdoc/>
    [MemoryPackIgnore]
    public WorldBuilder.Shared.Modules.Landscape.Services.ILandscapeCacheService LandscapeCacheService => _landscapeCacheService;

    public DocumentManager(IProjectRepository repo, IDatReaderWriter dats, ILogger<DocumentManager> logger, ILoggerFactory? loggerFactory = null) {
        _repo = repo;
        _dats = dats;
        _logger = logger;
        _landscapeDataProvider = new WorldBuilder.Shared.Modules.Landscape.Services.LandscapeDataProvider(repo, loggerFactory);
        _landscapeCacheService = new WorldBuilder.Shared.Modules.Landscape.Services.LandscapeCacheService();
        _cleanupTimer = new System.Threading.Timer(CleanupCache, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public async Task InitializeAsync(CancellationToken ct) {
        _logger.LogTrace("Initializing DocumentManager");
        await _repo.InitializeDatabaseAsync(ct);
        _logger.LogTrace("DocumentManager initialized");
    }

    public async Task<ITransaction> CreateTransactionAsync(CancellationToken ct) {
        return await _repo.CreateTransactionAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetDocumentIdsAsync(string prefix, ITransaction? tx, CancellationToken ct) {
        if (prefix.StartsWith("TerrainPatch_")) {
            var parts = prefix.Split('_');
            if (parts.Length > 1 && uint.TryParse(parts[1], out var regionId)) {
                return await _repo.GetTerrainPatchIdsAsync(regionId, tx, ct);
            }
        }
        return new List<string>();
    }

    public async Task<Result<DocumentRental<T>>> CreateDocumentAsync<T>(T document, ITransaction? tx = null,
        CancellationToken ct = default) where T : BaseDocument {
        if (_disposed) {
            return Result<DocumentRental<T>>.Failure("DocumentManager is disposed", "OBJECT_DISPOSED");
        }

        if (document == null) {
            return Result<DocumentRental<T>>.Failure("Document cannot be null", "ARGUMENT_NULL");
        }

        _logger.LogTrace("Creating document with ID: {DocumentId}, Type: {DocumentType}", document.Id, typeof(T).Name);

        await _cacheLock.WaitAsync(ct);
        try {
            if (_cache.ContainsKey(document.Id)) {
                _logger.LogWarning("Document with ID '{DocumentId}' already exists in cache", document.Id);
                return Result<DocumentRental<T>>.Failure($"Document with ID '{document.Id}' already exists in cache",
                    "DOCUMENT_EXISTS");
            }

            // Persist to database if it's a terrain patch
            if (document is TerrainPatchDocument tp) {
                var blob = tp.Serialize();
                // Extract regionId from ID: TerrainPatch_{regionId}_{chunkX}_{chunkY}
                var parts = TP_ID_REGEX().Match(document.Id);
                uint regionId = parts.Success ? uint.Parse(parts.Groups[1].Value) : 0;

                var insertResult = await _repo.UpsertTerrainPatchAsync(document.Id, regionId, blob, document.Version, tx, ct);
                if (insertResult.IsFailure) {
                    return Result<DocumentRental<T>>.Failure(insertResult.Error);
                }
                _logger.LogTrace("Terrain patch with ID {DocumentId} inserted into database", document.Id);
            }
            else if (document is LandscapeDocument) {
                _logger.LogTrace("Virtual LandscapeDocument with ID {DocumentId} added to system", document.Id);
            }
            else {
                return Result<DocumentRental<T>>.Failure($"Document type {typeof(T).Name} is not supported for generic persistence", "UNSUPPORTED_TYPE");
            }

            // Add to cache
            var entry = new DocumentCacheEntry(document);
            entry.IncrementRentCount(document);
            _cache[document.Id] = entry;
            _logger.LogTrace("Document with ID {DocumentId} added to cache", document.Id);

            return Result<DocumentRental<T>>.Success(new DocumentRental<T>(document,
                () => ReturnDocument(document.Id)));
        }
        finally {
            _cacheLock.Release();
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"TerrainPatch_(\d+)_")]
    private static partial System.Text.RegularExpressions.Regex TP_ID_REGEX();

    public async Task<Result<DocumentRental<T>>> RentDocumentAsync<T>(string id, ITransaction? tx = null, CancellationToken ct = default)
        where T : BaseDocument {
        if (_disposed) {
            return Result<DocumentRental<T>>.Failure("DocumentManager is disposed", "OBJECT_DISPOSED");
        }

        _logger.LogTrace("Renting document with ID: {DocumentId}", id);

        await _cacheLock.WaitAsync(ct);
        try {
            if (_cache.TryGetValue(id, out var entry)) {
                try {
                    var doc = (T)entry.Document;
                    entry.IncrementRentCount(doc);
                    _logger.LogTrace("Document with ID {DocumentId} found in cache (Instance: {Hash})", id, doc.GetHashCode());
                    return Result<DocumentRental<T>>.Success(new DocumentRental<T>(doc, () => ReturnDocument(id)));
                }
                catch (ObjectDisposedException) {
                    _logger.LogTrace("Document with ID {DocumentId} was garbage collected, removing from cache", id);
                    _cache.TryRemove(id, out _);
                }
            }

            _logger.LogTrace("Document with ID {DocumentId} not found in cache, loading/creating", id);
            T? newDoc = null;

            if (typeof(T) == typeof(LandscapeDocument) && id.StartsWith("LandscapeDocument_")) {
                _logger.LogTrace("Creating virtual LandscapeDocument for ID {DocumentId}", id);
                newDoc = new LandscapeDocument(id) as T;
            }
            else if (typeof(T) == typeof(TerrainPatchDocument)) {
                newDoc = await LoadDocumentAsync<T>(id, tx, ct);
            }

            if (newDoc == null) {
                _logger.LogTrace("Document with ID {DocumentId} not found or could not be created", id);
                return Result<DocumentRental<T>>.Failure($"Document with ID {id} not found",
                    "DOCUMENT_NOT_FOUND");
            }

            var newEntry = new DocumentCacheEntry(newDoc);
            newEntry.IncrementRentCount(newDoc);
            _cache[id] = newEntry;
            _logger.LogTrace("Document with ID {DocumentId} loaded/created and added to cache (Instance: {Hash})", id, newDoc.GetHashCode());

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

    public async Task<Result<Unit>> PersistDocumentAsync<T>(DocumentRental<T> rental, ITransaction? tx = null,
        CancellationToken ct = default) where T : BaseDocument {
        if (_disposed) {
            return Result<Unit>.Failure("DocumentManager is disposed", "OBJECT_DISPOSED");
        }

        if (rental == null) {
            return Result<Unit>.Failure("Rental cannot be null", "ARGUMENT_NULL");
        }

        var doc = rental.Document;

        if (doc is TerrainPatchDocument tp) {
            _logger.LogTrace("Persisting terrain patch with ID: {DocumentId}, Version: {Version} (Instance: {Hash})", doc.Id, doc.Version, doc.GetHashCode());
            var blob = tp.Serialize();
            var parts = TP_ID_REGEX().Match(doc.Id);
            uint regionId = parts.Success ? uint.Parse(parts.Groups[1].Value) : 0;

            var updateResult = await _repo.UpsertTerrainPatchAsync(doc.Id, regionId, blob, doc.Version, tx, ct);
            if (updateResult.IsFailure) {
                return Result<Unit>.Failure(updateResult.Error);
            }
            _logger.LogTrace("Terrain patch with ID {DocumentId} persisted to database", doc.Id);
        }
        else if (doc is LandscapeDocument) {
            _logger.LogTrace("LandscapeDocument {DocumentId} persist requested, skipping blob storage", doc.Id);
        }
        else {
            return Result<Unit>.Failure($"Document type {typeof(T).Name} is not supported for generic persistence", "UNSUPPORTED_TYPE");
        }

        return Result<Unit>.Success(Unit.Value);
    }

    public async Task<Result<bool>> ApplyLocalEventAsync(BaseCommand evt, ITransaction tx, CancellationToken ct) {
        if (_disposed) {
            return Result<bool>.Failure("DocumentManager is disposed", "OBJECT_DISPOSED");
        }

        if (evt == null) {
            return Result<bool>.Failure("Event cannot be null", "ARGUMENT_NULL");
        }

        _logger.LogTrace("Applying event {EventId} of type {EventType} for user {UserId}", evt.Id, evt.GetType().Name,
            UserId);

        evt.UserId = UserId;

        var oldTx = TransactionContext.Current;
        TransactionContext.Current = tx;
        try {
            var res = await evt.ApplyAsync(this, _dats, tx, ct);
            if (res.IsFailure) {
                _logger.LogError("Event {EventId} application failed: {Error}", evt.Id, res.Error);
                return Result<bool>.Failure(res.Error);
            }

            var insertEventResult = await _repo.InsertEventAsync(evt, tx, ct);
            if (insertEventResult.IsFailure) {
                return Result<bool>.Failure(insertEventResult.Error);
            }

            _logger.LogTrace("Event {EventId} applied successfully", evt.Id);

            return res;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error applying event {EventId} of type {EventType}", evt.Id, evt.GetType().Name);
            return Result<bool>.Failure($"Error applying event: {ex.Message}", "EVENT_APPLICATION_ERROR");
        }
        finally {
            TransactionContext.Current = oldTx;
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

        _logger.LogTrace("Applying event {EventId} of type {EventType} for user {UserId}", evt.Id, evt.GetType().Name,
            UserId);

        evt.UserId = UserId;

        var oldTx = TransactionContext.Current;
        TransactionContext.Current = tx;
        try {
            var res = await evt.ApplyResultAsync(this, _dats, tx, ct);
            if (res.IsFailure) {
                _logger.LogError("Event {EventId} application failed: {Error}", evt.Id, res.Error);
                return Result<TResult>.Failure(res.Error);
            }

            var insertEventResult = await _repo.InsertEventAsync(evt, tx, ct);
            if (insertEventResult.IsFailure) {
                return Result<TResult>.Failure(insertEventResult.Error);
            }

            _logger.LogTrace("Event {EventId} applied successfully", evt.Id);

            return res;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error applying event {EventId} of type {EventType}", evt.Id, evt.GetType().Name);
            return Result<TResult>.Failure($"Error applying event: {ex.Message}", "EVENT_APPLICATION_ERROR");
        }
        finally {
            TransactionContext.Current = oldTx;
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<LandscapeLayerBase>> GetLayersAsync(uint regionId, ITransaction? tx, CancellationToken ct) {
        return _repo.GetLayersAsync(regionId, tx, ct);
    }

    /// <inheritdoc/>
    public Task<Result<Unit>> UpsertLayerAsync(LandscapeLayerBase layer, uint regionId, int sortOrder, ITransaction? tx, CancellationToken ct) {
        return _repo.UpsertLayerAsync(layer, regionId, sortOrder, tx, ct);
    }

    /// <inheritdoc/>
    public Task<Result<Unit>> DeleteLayerAsync(string id, ITransaction? tx, CancellationToken ct) {
        return _repo.DeleteLayerAsync(id, tx, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<StaticObject>> GetStaticObjectsAsync(ushort? landblockId, uint? cellId, ITransaction? tx, CancellationToken ct) {
        return _repo.GetStaticObjectsAsync(landblockId, cellId, tx, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ushort>> GetAffectedLandblocksByLayerAsync(uint regionId, string layerId, ITransaction? tx, CancellationToken ct) {
        return _repo.GetAffectedLandblocksByLayerAsync(regionId, layerId, tx, ct);
    }

    /// <inheritdoc/>
    public Task<Result<Unit>> UpsertStaticObjectAsync(StaticObject obj, uint regionId, ushort? landblockId, uint? cellId, ITransaction? tx, CancellationToken ct) {
        if (obj.InstanceId.Type == ObjectType.Building) {
            var bldg = new BuildingObject {
                InstanceId = obj.InstanceId,
                ModelId = obj.ModelId,
                LayerId = obj.LayerId,
                Position = obj.Position,
                Rotation = obj.Rotation,
                IsDeleted = obj.IsDeleted
            };
            return _repo.UpsertBuildingAsync(bldg, regionId, landblockId, tx, ct);
        }
        return _repo.UpsertStaticObjectAsync(obj, regionId, landblockId, cellId, tx, ct);
    }

    /// <inheritdoc/>
    public Task<Result<Unit>> DeleteStaticObjectAsync(ObjectId instanceId, ITransaction? tx, CancellationToken ct) {
        if (instanceId.Type == ObjectType.Building) {
            return _repo.DeleteBuildingAsync(instanceId, tx, ct);
        }
        return _repo.DeleteStaticObjectAsync(instanceId, tx, ct);
    }

    /// <inheritdoc/>
    public Task<Result<Unit>> DeleteBuildingAsync(ObjectId instanceId, ITransaction? tx, CancellationToken ct) {
        return _repo.DeleteBuildingAsync(instanceId, tx, ct);
    }

    private async Task<T?> LoadDocumentAsync<T>(string id, ITransaction? tx, CancellationToken ct) where T : BaseDocument {
        _logger.LogTrace("Loading document with ID: {DocumentId} from database", id);

        if (typeof(T) == typeof(TerrainPatchDocument)) {
            var blobResult = await _repo.GetTerrainPatchBlobAsync(id, tx, ct);
            if (blobResult.IsFailure || blobResult.Value == null) {
                _logger.LogTrace("Terrain patch with ID {DocumentId} not found in database", id);
                return null;
            }
            _logger.LogTrace("Terrain patch with ID {DocumentId} loaded from database", id);
            return BaseDocument.Deserialize<T>(blobResult.Value);
        }

        return null;
    }

    private void CleanupCache(object? state) {
        if (_disposed) return;

        _cacheLock.Wait();
        try {
            var now = DateTime.UtcNow;
            var expiredThreshold = now.AddMinutes(-5);

            foreach (var kvp in _cache) {
                if (kvp.Value.RentCount == 0 && kvp.Value.LastAccessTime < expiredThreshold) {
                    if (_cache.TryRemove(kvp.Key, out var entry)) {
                        entry.Dispose();
                        _logger.LogTrace("Evicted document {DocumentId} from cache", kvp.Key);
                    }
                }
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error during cache cleanup");
        }
        finally {
            _cacheLock.Release();
        }
    }

    public void Dispose() {
        if (_disposed) return;
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

        public void IncrementRentCount(BaseDocument document) {
            Interlocked.Increment(ref _rentCount);
            _strongRef = document;
            LastAccessTime = DateTime.UtcNow;
        }

        public BaseDocument? DecrementRentCount() {
            var newCount = Interlocked.Decrement(ref _rentCount);
            LastAccessTime = DateTime.UtcNow;

            // Release strong reference when no longer rented
            if (newCount == 0) {
                var doc = _strongRef;
                _strongRef = null;
                return doc;
            }
            return null;
        }

        public void Dispose() {
            _strongRef?.Dispose();
            _strongRef = null;
        }

        public void MarkStale() {
            _isStale = true;
        }
    }
}
