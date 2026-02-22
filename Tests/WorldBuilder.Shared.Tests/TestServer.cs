using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using WorldBuilder.Server.Hubs;
using WorldBuilder.Server.Services;

namespace WorldBuilder.Shared.Tests {
    public class TestServer : IAsyncDisposable {
        private readonly WebApplication _app;
        private readonly int _port;

        public TestServer() {
            _port = GetAvailablePort();

            var options = new WebApplicationOptions {
                EnvironmentName = "Testing"
            };

            var builder = WebApplication.CreateBuilder(options);
            builder.Services.AddSignalR();
            builder.Services.AddSingleton<IWorldEventStore, InMemoryWorldEventStore>();

            builder.WebHost.ConfigureKestrel(kestrelOptions => {
                kestrelOptions.Listen(IPAddress.Loopback, _port, listenOptions => {
                    listenOptions.UseHttps();
                });
            });

            _app = builder.Build();
            _app.MapHub<WorldHub>("/world");
        }

        private static int GetAvailablePort() {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            socket.Listen(1);
            return ((IPEndPoint)socket.LocalEndPoint!).Port;
        }

        public async Task StartAsync() {
            await _app.StartAsync();
        }

        public string GetBaseUrl() {
            return $"https://localhost:{_port}";
        }

        public async ValueTask DisposeAsync() {
            await _app.StopAsync();
            await _app.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }
}