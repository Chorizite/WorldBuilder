using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Tests.Hubs {
    public class WorldHubIntegrationTests : IAsyncLifetime {
        private readonly TestServer _factory;
        private SignalRSyncClient? _client1;
        private SignalRSyncClient? _client2;

        public WorldHubIntegrationTests() {
            _factory = new TestServer();
        }

        public async Task InitializeAsync() {
            await _factory.StartAsync();

            var baseUrl = _factory.GetBaseUrl();
            _client1 = new SignalRSyncClient($"{baseUrl}/world");
            _client2 = new SignalRSyncClient($"{baseUrl}/world");

            await _client1.ConnectAsync(default);
            await _client2.ConnectAsync(default);
        }

        [Fact]
        public async Task BroadcastEvent_IsReceivedByOtherClients() {
            if (_client1 == null || _client2 == null) throw new InvalidOperationException();

            var evt = new TerrainUpdateCommand {
                UserId = Guid.NewGuid().ToString()
            };

            var received = new TaskCompletionSource<BaseCommand>();

            _ = Task.Run(async () => {
                await foreach (var e in _client2.ReceiveEventsAsync(CancellationToken.None)) {
                    received.TrySetResult(e);
                    break;
                }
            });

            await _client1.SendEventAsync(evt, default);

            var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.Equal(evt.Id, result.Id);
        }

        public async Task DisposeAsync() {
            if (_client1 != null) await _client1.DisposeAsync();
            if (_client2 != null) await _client2.DisposeAsync();
            await _factory.DisposeAsync();
        }
    }
}