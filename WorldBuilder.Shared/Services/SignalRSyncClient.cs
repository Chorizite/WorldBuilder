using MemoryPack;
using Microsoft.AspNetCore.SignalR.Client;
using System.Threading.Channels;
using WorldBuilder.Shared.Hubs;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Shared.Services {
    public class SignalRSyncClient : ISyncClient, IAsyncDisposable {
        private HubConnection _connection = null!;
        private readonly string _url;
        private readonly Channel<BaseCommand> _incoming = Channel.CreateUnbounded<BaseCommand>();

        public SignalRSyncClient(string url) => _url = url;

        public async Task ConnectAsync(CancellationToken ct) {
            _connection = new HubConnectionBuilder()
                .WithUrl(_url, options => {
                    options.HttpMessageHandlerFactory = _ => new HttpClientHandler {
                        ServerCertificateCustomValidationCallback =
                            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                    };
                })
                .Build();

            // Clean, type-safe subscription
            _connection.OnDocumentEvent(evt => _incoming.Writer.TryWrite(evt));

            await _connection.StartAsync(ct);
        }

        public async IAsyncEnumerable<BaseCommand> ReceiveEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken ct) {
            await foreach (var evt in _incoming.Reader.ReadAllAsync(ct))
                yield return evt;
        }

        public async Task SendEventAsync(BaseCommand evt, CancellationToken ct)
            => await _connection.SendDocumentEventAsync(evt, ct);

        public async Task<ulong> GetServerTimeAsync(CancellationToken ct)
            => await _connection.InvokeAsync<ulong>(HubMethods.GetServerTime, ct);

        public async Task DisconnectAsync() {
            if (_connection is not null)
                await _connection.DisposeAsync();
        }

        public async ValueTask DisposeAsync() => await DisconnectAsync();

        public async Task<IReadOnlyList<BaseCommand>> GetEventsSinceAsync(ulong lastServerTimestamp,
            CancellationToken ct)
            => await _connection.GetEventsSinceAsync(lastServerTimestamp, ct);
    }
}