using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Diagnostics;
using WorldBuilder.Shared.Hubs;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Server.Hubs {
    public class WorldHub : Hub<IWorldHubClient>, IWorldHub {
        // In-memory event store with server timestamps
        // In production, this should be replaced with a persistent store
        private static readonly ConcurrentDictionary<ulong, byte[]> _eventStore = new();
        private static ulong _nextTimestamp = 1;
        private static readonly object _timestampLock = new();

        public async Task ReceiveDocumentEvent(byte[] data) {
            var evt = BaseCommand.Deserialize(data);
            if (evt == null) return;

            // Assign server timestamp
            ulong serverTimestamp;
            lock (_timestampLock) {
                serverTimestamp = _nextTimestamp++;
            }

            evt.ServerTimestamp = serverTimestamp;

            // Store the event
            var serialized = evt.Serialize();
            _eventStore[serverTimestamp] = serialized;

            // Broadcast to all other clients
            await Clients.AllExcept(Context.ConnectionId).DocumentEventReceived(serialized);
        }

        public Task<ulong> GetServerTime() {
            lock (_timestampLock) {
                return Task.FromResult(_nextTimestamp);
            }
        }

        public Task<byte[][]> GetEventsSince(ulong lastServerTimestamp) {
            var events = _eventStore
                .Where(kv => kv.Key > lastServerTimestamp)
                .OrderBy(kv => kv.Key)
                .Select(kv => kv.Value)
                .ToArray();
            return Task.FromResult(events);
        }
    }
}