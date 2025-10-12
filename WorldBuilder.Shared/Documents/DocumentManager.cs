using DatReaderWriter;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading.Channels;
using WorldBuilder.Shared.Lib;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace WorldBuilder.Shared.Documents {
    public class DocumentManager : IDisposable {
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        public readonly IDocumentStorageService DocumentStorageService;
        private readonly ILogger<DocumentManager> _logger;
        public IDatReaderWriter Dats { get; set; }
        private readonly ConcurrentDictionary<string, BaseDocument> _activeDocs = new();
        private readonly Guid _clientId = Guid.NewGuid();

        // Batching for updates
        private readonly Channel<DocumentUpdate> _updateQueue;
        private readonly ChannelWriter<DocumentUpdate> _updateWriter;
        private readonly ChannelReader<DocumentUpdate> _updateReader;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly Task _batchProcessor;

        // Configuration
        private readonly TimeSpan _batchInterval = TimeSpan.FromSeconds(2); // Batch every 2 seconds
        private readonly int _maxBatchSize = 50; // Max updates per batch

        public Guid ClientId => _clientId;

        private record DocumentUpdate(string DocumentId, BaseDocument Document, DateTime Timestamp);

        private string? _cacheDirectory;

        public DocumentManager(IDocumentStorageService documentService, ILogger<DocumentManager> logger) {
            DocumentStorageService = documentService;
            _logger = logger;

            // Initialize update batching
            var options = new BoundedChannelOptions(1000) {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            };
            _updateQueue = Channel.CreateBounded<DocumentUpdate>(options);
            _updateWriter = _updateQueue.Writer;
            _updateReader = _updateQueue.Reader;

            // Start batch processor on a dedicated background thread
            _batchProcessor = Task.Factory.StartNew(
                () => ProcessUpdateBatchesAsync(_cancellationTokenSource.Token),
                _cancellationTokenSource.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap();
        }

        public void SetCacheDirectory(string cacheDirectory) {
            _cacheDirectory = cacheDirectory;
            foreach (var doc in _activeDocs.Values.ToArray()) {
                doc.SetCacheDirectory(cacheDirectory);
            }
        }

        /// <summary>
        /// Non-generic overload for creating or getting a document by explicit type.
        /// </summary>
        public async Task<BaseDocument?> GetOrCreateDocumentAsync(string documentId, Type docType) {
            if (!typeof(BaseDocument).IsAssignableFrom(docType)) {
                _logger.LogError("Invalid document type {DocType} for document {DocumentId}; must inherit BaseDocument", docType.Name, documentId);
                return null;
            }
            return await CreateOrLoadDocumentInternalAsync(documentId, docType);
        }

        /// <summary>
        /// Generic overload (original, now delegates to internal for reuse).
        /// </summary>
        public async Task<T?> GetOrCreateDocumentAsync<T>(string documentId) where T : BaseDocument {
            var doc = await CreateOrLoadDocumentInternalAsync(documentId, typeof(T));
            if (doc is T typedDoc) {
                return typedDoc;
            }
            if (doc != null) {
                _logger.LogError("Document {DocumentId}({ActualType}) is not of expected type {ExpectedType}",
                    documentId, doc.GetType().Name, typeof(T).Name);
            }
            return null;
        }

        private async Task<BaseDocument?> CreateOrLoadDocumentInternalAsync(string documentId, Type docType) {
            var docTypeName = docType.Name;

            // Try to get from cache first
            if (_activeDocs.TryGetValue(documentId, out var doc)) {
                if (doc.GetType() != docType) {  // Exact type match, mirroring original 'is not T'
                    _logger.LogError("Document {DocumentId}({ActualType}) is not of type {ExpectedType}",
                        documentId, doc.GetType().Name, docTypeName);
                    return null;
                }
                _logger.LogInformation("Pulling Document {DocumentId}({Type}) from cache", documentId, doc.GetType().Name);
                return doc;
            }

            try {
                var dbDoc = await DocumentStorageService.GetDocumentAsync(documentId);
                var docInstance = (BaseDocument?)Activator.CreateInstance(docType, _logger);

                if (docInstance == null) {
                    _logger.LogError("Failed to create document {DocumentId} of type {Type}", documentId, docTypeName);
                    return null;
                }
                docInstance.Id = documentId;
                docInstance.SetCacheDirectory(_cacheDirectory);

                if (dbDoc == null) {
                    dbDoc = await DocumentStorageService.CreateDocumentAsync(documentId, docTypeName, docInstance.SaveToProjection());
                    _logger.LogInformation("Creating new Document {DocumentId}({Type})", documentId, docTypeName);
                }
                else {
                    if (!docInstance.LoadFromProjection(dbDoc.Data)) {
                        _logger.LogError("Failed to load projection for document {DocumentId}", documentId);
                        return null;
                    }
                }

                if (!await docInstance.InitAsync(Dats)) {
                    _logger.LogError("Failed to init document {DocumentId} of type {Type}", documentId, docTypeName);
                    return null;
                }

                // Add to cache, ensuring only one instance per documentId
                if (!_activeDocs.TryAdd(documentId, docInstance)) {
                    // If another thread added it first, retrieve it
                    if (_activeDocs.TryGetValue(documentId, out var existingDoc) && existingDoc.GetType() == docType) {
                        return existingDoc;
                    }
                    _logger.LogError("Failed to add document {DocumentId} of type {Type}", documentId, docTypeName);
                    return null;
                }

                docInstance.Update += HandleDocumentUpdate;
                return docInstance;
            }
            catch (Exception ex) {
                _logger.LogError(ex.ToString());
                _logger.LogError(ex, "Failed to create document {DocumentId} of type {Type}", documentId, docTypeName);
                return null;
            }
        }

        private void HandleDocumentUpdate(object? sender, UpdateEventArgs e) {
            // Queue the update for batching
            var update = new DocumentUpdate(e.Document.Id, e.Document, DateTime.UtcNow);

            if (!_updateWriter.TryWrite(update)) {
                // If queue is full, start a non-blocking task to wait and retry
                Task.Run(async () => {
                    try {
                        await _updateWriter.WriteAsync(update, _cancellationTokenSource.Token);
                    }
                    catch (Exception ex) {
                        _logger.LogError(ex, "Failed to queue update for document {DocumentId}", e.Document.Id);
                        // Fallback: save immediately to avoid data loss
                        try {
                            var projection = e.Document.SaveToProjection();
                            await DocumentStorageService.UpdateDocumentAsync(e.Document.Id, projection);
                        }
                        catch (Exception ex2) {
                            _logger.LogError(ex2, "Failed to process immediate update for document {DocumentId}", e.Document.Id);
                        }
                    }
                }, _cancellationTokenSource.Token);
            }
        }

        private async Task ProcessUpdateBatchesAsync(CancellationToken cancellationToken) {
            var batch = new List<DocumentUpdate>();
            try {
                while (!cancellationToken.IsCancellationRequested) {
                    batch.Clear();
                    if (await _updateReader.WaitToReadAsync(cancellationToken)) {
                        var batchStartTime = DateTime.UtcNow;
                        var batchTimeout = batchStartTime.Add(_batchInterval);
                        int updateCount = 0;

                        while (updateCount < _maxBatchSize && DateTime.UtcNow < batchTimeout) {
                            if (_updateReader.TryRead(out var update)) {
                                batch.Add(update);
                                updateCount++;
                            }
                            else if (batch.Count == 0) {
                                await Task.Delay(50, cancellationToken);
                            }
                            else {
                                break; // Process what we have
                            }
                        }

                        // Process immediately if we have updates and the queue is empty
                        if (batch.Count > 0 && !_updateReader.TryRead(out _)) {
                            await ProcessBatch(batch);
                        }
                        else if (batch.Count > 0) {
                            await ProcessBatch(batch);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) {
                _logger.LogError(ex, "Error in batch processor");
                if (!cancellationToken.IsCancellationRequested) {
                    await Task.Delay(1000, cancellationToken);
                    await ProcessUpdateBatchesAsync(cancellationToken); // Consider a loop instead
                }
            }
        }

        private async Task ProcessBatch(List<DocumentUpdate> batch) {
            try {
                _logger.LogInformation("Processing batch of {Count} updates", batch.Count);
                var latestUpdates = batch
                    .GroupBy(u => u.DocumentId)
                    .Select(g => g.OrderByDescending(u => u.Timestamp).First())
                    .ToList();

                var semaphore = new SemaphoreSlim(16); // Adjustable concurrency limit
                var tasks = latestUpdates.Select(async update => {
                    _logger.LogInformation("Processing update for document {DocumentId}", update.DocumentId);
                    await semaphore.WaitAsync();
                    try {
                        if (!update.Document.IsDirty) return;
                        var projection = update.Document.SaveToProjection();
                        await DocumentStorageService.UpdateDocumentAsync(update.DocumentId, projection);
                    }
                    catch (Exception ex) {
                        _logger.LogError(ex, "Failed to process batched update for document {DocumentId}", update.DocumentId);
                    }
                    finally {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
                _logger.LogDebug("Processed batch of {Count} updates (deduplicated from {OriginalCount})", latestUpdates.Count, batch.Count);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Failed to process update batch");
            }
        }

        public async Task CloseDocumentAsync(string documentId) {
            if (_activeDocs.TryRemove(documentId, out var doc)) {
                doc.Update -= HandleDocumentUpdate;
                try {
                    if (!doc.IsDirty) return;
                    var projection = doc.SaveToProjection();
                    await DocumentStorageService.UpdateDocumentAsync(documentId, projection);
                }
                catch (Exception ex) {
                    _logger.LogError(ex, "Failed to save document {DocumentId} on close", documentId);
                }
                _logger.LogInformation("Closing Document {DocumentId}({Type})", documentId, doc.GetType().Name);
            }
            else {
                _logger.LogWarning("CloseDocumentAsync: Document {DocumentId} not found in cache", documentId);
            }
        }

        public async Task FlushPendingUpdatesAsync() {
            // Collect and process all pending updates asynchronously
            var remainingUpdates = new List<DocumentUpdate>();
            await foreach (var update in _updateReader.ReadAllAsync(_cancellationTokenSource.Token)) {
                remainingUpdates.Add(update);
            }

            if (remainingUpdates.Count > 0) {
                await ProcessBatch(remainingUpdates);
            }
        }

        public void Dispose() {
            try {
                // Signal cancellation and complete the writer
                _cancellationTokenSource.Cancel();
                _updateWriter.TryComplete();

                // Process remaining updates asynchronously
                Task.Run(async () => {
                    await FlushPendingUpdatesAsync();
                }).GetAwaiter().GetResult(); // Synchronous wait in Dispose is acceptable as it's cleanup

                // Wait for batch processor to complete with a timeout
                _batchProcessor.Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "Error during DocumentManager disposal");
            }
            finally {
                _cancellationTokenSource.Dispose();
                _activeDocs.Clear();
                DocumentStorageService.Dispose();
            }
        }
    }
}