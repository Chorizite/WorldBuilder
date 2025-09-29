using DatReaderWriter;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading.Channels;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Shared.Documents {
    public class DocumentManager : IDisposable {
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private readonly IDocumentStorageService _documentService;
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

        public DocumentManager(IDocumentStorageService documentService, ILogger<DocumentManager> logger) {
            _documentService = documentService;
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

        public async Task<T?> GetOrCreateDocumentAsync<T>(string documentId) where T : BaseDocument {
            // Try to get from cache first
            if (_activeDocs.TryGetValue(documentId, out var doc)) {
                if (doc is not T existingTDoc) {
                    _logger.LogError("Document {DocumentId}({Type}) is not of type {ExpectedType}", documentId, doc.GetType().Name, typeof(T));
                    return null;
                }
                _logger.LogInformation("Pulling Document {DocumentId}({Type}) from cache", documentId, doc.GetType().Name);
                return existingTDoc;
            }

            try {
                var tType = typeof(T).Name;
                var dbDoc = await _documentService.GetDocumentAsync(documentId);
                var tDoc = Activator.CreateInstance(typeof(T), _logger) as T;

                if (tDoc is null) {
                    _logger.LogError("Failed to create 1 document {DocumentId} of type {Type}", documentId, tType);
                    return null;
                }
                tDoc.Id = documentId;

                if (dbDoc == null) {
                    dbDoc = await _documentService.CreateDocumentAsync(documentId, tType, tDoc.SaveToProjection());
                    _logger.LogInformation("Creating new Document {DocumentId}({Type})", documentId, typeof(T));
                }
                else {
                    if (!tDoc.LoadFromProjection(dbDoc.Data)) {
                        _logger.LogError("Failed to load projection for document {DocumentId}", documentId);
                        return null;
                    }
                }

                if (!await tDoc.InitAsync(Dats)) {
                    _logger.LogError("Failed to init document {DocumentId} of type {Type}", documentId, tType);
                    return null;
                }

                // Add to cache, ensuring only one instance per documentId
                if (!_activeDocs.TryAdd(documentId, tDoc)) {
                    // If another thread added it first, retrieve it
                    if (_activeDocs.TryGetValue(documentId, out var existingDoc) && existingDoc is T existingT) {
                        return existingT;
                    }
                    _logger.LogError("Failed to add document {DocumentId} of type {Type}", documentId, tType);
                    return null;
                }

                tDoc.Update += HandleDocumentUpdate;
                return tDoc;
            }
            catch (Exception ex) {
                _logger.LogError(ex.ToString());
                _logger.LogError(ex, "Failed to create 2 document {DocumentId} of type {Type}", documentId, typeof(T).Name);
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
                            await _documentService.UpdateDocumentAsync(e.Document.Id, projection);
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
                var latestUpdates = batch
                    .GroupBy(u => u.DocumentId)
                    .Select(g => g.OrderByDescending(u => u.Timestamp).First())
                    .ToList();

                var semaphore = new SemaphoreSlim(16); // Adjustable concurrency limit
                var tasks = latestUpdates.Select(async update => {
                    await semaphore.WaitAsync();
                    try {
                        var projection = update.Document.SaveToProjection();
                        await _documentService.UpdateDocumentAsync(update.DocumentId, projection);
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
                    var projection = doc.SaveToProjection();
                    await _documentService.UpdateDocumentAsync(documentId, projection);
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
                _documentService.Dispose();
            }
        }
    }
}