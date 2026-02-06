using MemoryPack;
using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Shared.Hubs {
    public static class HubMethods {
        public const string DocumentEventReceived = nameof(DocumentEventReceived);
        public const string ReceiveDocumentEvent = nameof(ReceiveDocumentEvent);
        public const string GetServerTime = nameof(GetServerTime);
        public const string GetEventsSince = nameof(GetEventsSince);
    }


    public static class HubConnectionExtensions {
        public static IDisposable OnDocumentEvent(
            this HubConnection connection,
            Action<BaseCommand> handler) {
            return connection.On<byte[]>(HubMethods.DocumentEventReceived, data => {
                var evt = BaseCommand.Deserialize(data);
                if (evt != null)
                    handler(evt);
            });
        }

        public static Task SendDocumentEventAsync(this HubConnection connection, BaseCommand evt,
            CancellationToken ct = default) {
            return connection.InvokeAsync(HubMethods.ReceiveDocumentEvent, evt.Serialize(), ct);
        }

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