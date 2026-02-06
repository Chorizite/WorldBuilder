using Microsoft.Extensions.Logging;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Repositories;

namespace WorldBuilder.Shared.Services;

public class SyncService {
    private readonly IDocumentManager _docManager;
    private readonly ISyncClient _client;
    private readonly IProjectRepository _repo;
    private readonly IDatReaderWriter _dats;
    private readonly ILogger<SyncService>? _logger;
    private readonly string _userId;
    private ulong _lastServerTimestamp = 0;
    private bool _isOnline = false;

    public SyncService(
        IDocumentManager docManager,
        ISyncClient client,
        IProjectRepository repo,
        IDatReaderWriter dats,
        string userId,
        ILogger<SyncService>? logger = null) {
        _docManager = docManager;
        _client = client;
        _repo = repo;
        _dats = dats;
        _userId = userId;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct) {
        _logger?.LogInformation("Starting sync service");
        await _client.ConnectAsync(ct);
        _isOnline = true;

        // First sync any events we missed while offline
        await SyncFromServerAsync(ct);

        // Then send any unsynced local events
        await SendUnsyncedEventsAsync(ct);

        // Start background loops
        _ = Task.Run(() => ReceiveLoopAsync(ct), ct);
        _ = Task.Run(() => SendLoopAsync(ct), ct);

        _logger?.LogInformation("Sync service started");
    }

    public async Task<TResult> ApplyLocalEventAsync<TResult>(BaseCommand<TResult> evt, CancellationToken ct) {
        if (string.IsNullOrEmpty(evt.Id))
            throw new InvalidOperationException("Event Id cannot be null or empty");

        evt.UserId = _userId;
        evt.ClientTimestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var tx = await _docManager.CreateTransactionAsync(ct);
        try {
            var result = await _docManager.ApplyLocalEventAsync(evt, tx, ct);
            if (result.IsFailure) {
                throw new InvalidOperationException($"Failed to apply event: {result.Error.Message}");
            }

            await tx.CommitAsync(ct);

            // Send to server if online
            if (_isOnline) {
                try {
                    await _client.SendEventAsync(evt, ct);
                    // Update server timestamp after successful send
                    var updateTx = await _repo.CreateTransactionAsync(ct);
                    await _repo.UpdateEventServerTimestampAsync(evt.Id, evt.ServerTimestamp ?? evt.ClientTimestamp,
                        updateTx, ct);
                    await updateTx.CommitAsync(ct);
                }
                catch (Exception ex) {
                    _logger?.LogWarning(ex, "Failed to send event to server, will retry later");
                }
            }

            return result.Value;
        }
        catch {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private async Task SyncFromServerAsync(CancellationToken ct) {
        _logger?.LogInformation("Syncing from server since timestamp {Timestamp}", _lastServerTimestamp);
        try {
            var missedEvents = await _client.GetEventsSinceAsync(_lastServerTimestamp, ct);
            foreach (var evt in missedEvents) {
                // Skip our own events
                if (evt.UserId == _userId) continue;

                await ApplyRemoteEventAsync(evt, ct);

                if (evt.ServerTimestamp.HasValue && evt.ServerTimestamp.Value > _lastServerTimestamp) {
                    _lastServerTimestamp = evt.ServerTimestamp.Value;
                }
            }

            _logger?.LogInformation("Synced {Count} events from server", missedEvents.Count);
        }
        catch (Exception ex) {
            _logger?.LogError(ex, "Failed to sync from server");
        }
    }

    private async Task SendUnsyncedEventsAsync(CancellationToken ct) {
        var unsyncedEvents = await _repo.GetUnsyncedEventsAsync(ct);
        _logger?.LogInformation("Sending {Count} unsynced events", unsyncedEvents.Count);

        foreach (var evt in unsyncedEvents) {
            try {
                await _client.SendEventAsync(evt, ct);
                var tx = await _repo.CreateTransactionAsync(ct);
                await _repo.UpdateEventServerTimestampAsync(evt.Id, evt.ServerTimestamp ?? evt.ClientTimestamp, tx, ct);
                await tx.CommitAsync(ct);
            }
            catch (Exception ex) {
                _logger?.LogWarning(ex, "Failed to send event {EventId}", evt.Id);
                break; // Stop on first failure to maintain order
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct) {
        _logger?.LogInformation("Starting receive loop");
        await foreach (var evt in _client.ReceiveEventsAsync(ct)) {
            try {
                // Skip our own events
                if (evt.UserId == _userId) continue;

                await ApplyRemoteEventAsync(evt, ct);

                if (evt.ServerTimestamp.HasValue && evt.ServerTimestamp.Value > _lastServerTimestamp) {
                    _lastServerTimestamp = evt.ServerTimestamp.Value;
                }
            }
            catch (Exception ex) {
                _logger?.LogError(ex, "Error processing remote event {EventId}", evt.Id);
            }
        }
    }

    private async Task ApplyRemoteEventAsync(BaseCommand evt, CancellationToken ct) {
        _logger?.LogDebug("Applying remote event {EventId}", evt.Id);
        var tx = await _docManager.CreateTransactionAsync(ct);
        try {
            var result = await evt.ApplyAsync(_docManager, _dats, tx, ct);
            if (result.IsFailure) {
                _logger?.LogWarning("Failed to apply remote event {EventId}: {Error}", evt.Id, result.Error.Message);
                await tx.RollbackAsync(ct);
                return;
            }

            await tx.CommitAsync(ct);
        }
        catch (Exception ex) {
            _logger?.LogError(ex, "Error applying remote event {EventId}", evt.Id);
            await tx.RollbackAsync(ct);
        }
    }

    private async Task SendLoopAsync(CancellationToken ct) {
        // This loop periodically checks for unsynced events and sends them
        // This handles cases where sending failed initially
        while (!ct.IsCancellationRequested) {
            await Task.Delay(5000, ct); // Check every 5 seconds

            if (!_isOnline) continue;

            try {
                await SendUnsyncedEventsAsync(ct);
            }
            catch (Exception ex) {
                _logger?.LogWarning(ex, "Error in send loop");
            }
        }
    }

    public async Task StopAsync() {
        _logger?.LogInformation("Stopping sync service");
        _isOnline = false;
        await _client.DisconnectAsync();
    }
}
