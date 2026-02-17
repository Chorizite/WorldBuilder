using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Diagnostics;
using WorldBuilder.Shared.Hubs;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Server.Services;

namespace WorldBuilder.Server.Hubs {
    public class WorldHub : Hub<IWorldHubClient>, IWorldHub {
        private readonly IWorldEventStore _eventStore;

        public WorldHub(IWorldEventStore eventStore) {
            _eventStore = eventStore;
        }

        public async Task ReceiveDocumentEvent(byte[] data) {
            var evt = BaseCommand.Deserialize(data);
            if (evt == null) return;

            // Assign server timestamp
            ulong serverTimestamp = _eventStore.IncrementNextTimestamp();

            evt.ServerTimestamp = serverTimestamp;

            // Store the event
            var serialized = evt.Serialize();
            _eventStore.StoreEvent(serverTimestamp, serialized);

            // Broadcast to all other clients
            await Clients.AllExcept(Context.ConnectionId).DocumentEventReceived(serialized);
        }

        public Task<ulong> GetServerTime() {
            return Task.FromResult(_eventStore.GetNextTimestamp());
        }

        public Task<byte[][]> GetEventsSince(ulong lastServerTimestamp) {
            var events = _eventStore.GetEventsSince(lastServerTimestamp);
            return Task.FromResult(events);
        }
    }
}