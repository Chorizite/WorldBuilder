using System.Collections.Concurrent;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Tests.Mocks {
    internal class MockSyncClient : ISyncClient {
        public bool IsConnected { get; set; }
        public readonly ConcurrentQueue<BaseCommand> SentEvents = new();
        public readonly ConcurrentQueue<BaseCommand> IncomingEvents = new();
        public readonly ConcurrentBag<BaseCommand> StoredEvents = new();
        private ulong _serverTimestamp = 100;

        public Task ConnectAsync(CancellationToken ct) {
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync() {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<BaseCommand> ReceiveEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken ct) {
            while (!ct.IsCancellationRequested) {
                if (IncomingEvents.TryDequeue(out var evt))
                    yield return evt;
                else
                    await Task.Delay(10, ct);
            }
        }

        public Task SendEventAsync(BaseCommand evt, CancellationToken ct) {
            // Simulate server assigning timestamp
            evt.ServerTimestamp = ++_serverTimestamp;
            SentEvents.Enqueue(evt);
            StoredEvents.Add(evt);
            return Task.CompletedTask;
        }

        public Task<ulong> GetServerTimeAsync(CancellationToken ct) => Task.FromResult(_serverTimestamp);

        public Task<IReadOnlyList<BaseCommand>> GetEventsSinceAsync(ulong lastServerTimestamp, CancellationToken ct) {
            var events = StoredEvents
                .Where(e => e.ServerTimestamp.HasValue && e.ServerTimestamp.Value > lastServerTimestamp)
                .OrderBy(e => e.ServerTimestamp)
                .ToList();
            return Task.FromResult<IReadOnlyList<BaseCommand>>(events);
        }
    }
}