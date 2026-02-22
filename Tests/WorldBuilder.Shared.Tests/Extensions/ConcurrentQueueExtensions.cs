using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorldBuilder.Shared.Tests.Extensions {
    internal static class QueueExtensions {
        public static async Task<T> DequeueAsync<T>(this ConcurrentQueue<T> queue, TimeSpan timeout) {
            var cts = new CancellationTokenSource(timeout);
            while (!cts.IsCancellationRequested) {
                if (queue.TryDequeue(out var item))
                    return item;
                await Task.Delay(10, cts.Token);
            }
            throw new TimeoutException("Dequeue timed out");
        }
    }
}