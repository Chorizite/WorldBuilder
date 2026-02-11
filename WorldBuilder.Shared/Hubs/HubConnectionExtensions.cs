using MemoryPack;
using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Shared.Hubs {
    /// <summary>
    /// Constants for Hub method names.
    /// </summary>
    public static class HubMethods {
        /// <summary>DocumentEventReceived method name.</summary>
        public const string DocumentEventReceived = nameof(DocumentEventReceived);
        /// <summary>ReceiveDocumentEvent method name.</summary>
        public const string ReceiveDocumentEvent = nameof(ReceiveDocumentEvent);
        /// <summary>GetServerTime method name.</summary>
        public const string GetServerTime = nameof(GetServerTime);
        /// <summary>GetEventsSince method name.</summary>
        public const string GetEventsSince = nameof(GetEventsSince);
    }


    /// <summary>
    /// Extension methods for <see cref="HubConnection"/>.
    /// </summary>
    public static class HubConnectionExtensions {
        /// <summary>
        /// Registers a handler for document events.
        /// </summary>
        /// <param name="connection">The hub connection.</param>
        /// <param name="handler">The handler for the received command.</param>
        /// <returns>A disposable that unregisters the handler when disposed.</returns>
        public static IDisposable OnDocumentEvent(
            this HubConnection connection,
            Action<BaseCommand> handler) {
            return connection.On<byte[]>(HubMethods.DocumentEventReceived, data => {
                var evt = BaseCommand.Deserialize(data);
                if (evt != null)
                    handler(evt);
            });
        }

        /// <summary>
        /// Sends a document event asynchronously.
        /// </summary>
        /// <param name="connection">The hub connection.</param>
        /// <param name="evt">The command to send.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public static Task SendDocumentEventAsync(this HubConnection connection, BaseCommand evt,
            CancellationToken ct = default) {
            return connection.InvokeAsync(HubMethods.ReceiveDocumentEvent, evt.Serialize(), ct);
        }

        /// <summary>
        /// Gets all events since a specific server timestamp asynchronously.
        /// </summary>
        /// <param name="connection">The hub connection.</param>
        /// <param name="lastServerTimestamp">The timestamp to get events from.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task containing a list of received base commands.</returns>
        public static async Task<IReadOnlyList<BaseCommand>> GetEventsSinceAsync(this HubConnection connection,
            ulong lastServerTimestamp, CancellationToken ct = default) {
            var result = new List<BaseCommand>();
            var rawEvents = await connection.InvokeAsync<byte[][]>(HubMethods.GetEventsSince, lastServerTimestamp, ct);
            foreach (var data in rawEvents) {
                var evt = BaseCommand.Deserialize(data);
                if (evt != null)
                    result.Add(evt);
            }

            return result;
        }
    }
}