using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace WorldBuilder.Shared.Lib {
    /// <summary>
    /// Utility to debounce actions, ensuring only the last call within a window is executed.
    /// </summary>
    /// <typeparam name="TKey">The type used to distinguish different debounced actions.</typeparam>
    public class DebouncedAction<TKey> where TKey : notnull {
        private readonly ConcurrentDictionary<TKey, CancellationTokenSource> _debounceTokens = new();
        private readonly TimeSpan _delay;

        public DebouncedAction(TimeSpan delay) {
            _delay = delay;
        }

        /// <summary>
        /// Requests an action to be executed after the configured delay.
        /// If another request with the same key is made before the delay elapses, the previous one is cancelled.
        /// </summary>
        public void Request(TKey key, Action action) {
            // Cancel previous request for this key
            if (_debounceTokens.TryRemove(key, out var cts)) {
                cts.Cancel();
                cts.Dispose();
            }

            var newCts = new CancellationTokenSource();
            _debounceTokens[key] = newCts;

            Task.Delay(_delay, newCts.Token).ContinueWith(t => {
                if (t.IsCompletedSuccessfully && !newCts.IsCancellationRequested) {
                    _debounceTokens.TryRemove(key, out _);
                    action();
                }
                newCts.Dispose();
            }, TaskScheduler.Default);
        }

        /// <summary>
        /// Requests an async action to be executed after the configured delay.
        /// </summary>
        public void RequestAsync(TKey key, Func<Task> action) {
            if (_debounceTokens.TryRemove(key, out var cts)) {
                cts.Cancel();
                cts.Dispose();
            }

            var newCts = new CancellationTokenSource();
            _debounceTokens[key] = newCts;

            Task.Delay(_delay, newCts.Token).ContinueWith(async t => {
                if (t.IsCompletedSuccessfully && !newCts.IsCancellationRequested) {
                    _debounceTokens.TryRemove(key, out _);
                    await action();
                }
                newCts.Dispose();
            }, TaskScheduler.Default);
        }

        public void Cancel(TKey key) {
            if (_debounceTokens.TryRemove(key, out var cts)) {
                cts.Cancel();
                cts.Dispose();
            }
        }

        public void CancelAll() {
            foreach (var key in _debounceTokens.Keys) {
                if (_debounceTokens.TryRemove(key, out var cts)) {
                    cts.Cancel();
                    cts.Dispose();
                }
            }
        }
    }
}
