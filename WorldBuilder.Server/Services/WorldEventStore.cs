using System.Collections.Concurrent;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Server.Services {
    public interface IWorldEventStore {
        ulong GetNextTimestamp();
        ulong IncrementNextTimestamp();
        void StoreEvent(ulong timestamp, byte[] data);
        byte[][] GetEventsSince(ulong lastServerTimestamp);
    }

    public class InMemoryWorldEventStore : IWorldEventStore {
        private readonly ConcurrentDictionary<ulong, byte[]> _eventStore = new();
        private ulong _nextTimestamp = 1;
        private readonly object _timestampLock = new();

        public ulong GetNextTimestamp() {
            lock (_timestampLock) {
                return _nextTimestamp;
            }
        }

        public ulong IncrementNextTimestamp() {
            lock (_timestampLock) {
                return _nextTimestamp++;
            }
        }

        public void StoreEvent(ulong timestamp, byte[] data) {
            _eventStore[timestamp] = data;
        }

        public byte[][] GetEventsSince(ulong lastServerTimestamp) {
            return _eventStore
                .Where(kv => kv.Key > lastServerTimestamp)
                .OrderBy(kv => kv.Key)
                .Select(kv => kv.Value)
                .ToArray();
        }
    }
}
