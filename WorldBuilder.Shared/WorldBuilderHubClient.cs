using System.Linq;
using System.Text;
using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WorldBuilder.Shared.Messages;

namespace WorldBuilder.Shared {

    public class WorldBuilderHubClient : IWorldBuilderHubClient {
        private HubConnection? _hubConnection;
        private bool _disposed = false;
        private bool _isSyncedWithServer;
        private bool _hasJoinedProject;

        public event Func<UpdateMessage, Task>? UpdateReceived;

        public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

        public bool IsSyncedWithServer {
            get => IsConnected && HasJoinedProject && _isSyncedWithServer;
            private set => _isSyncedWithServer = value;
        }

        public bool HasJoinedProject {
            get => IsConnected && _hasJoinedProject;
            private set => _hasJoinedProject = value;
        }

        public string Username { get; private set; }

        public string ServerUrl { get; private set; }

        public WorldBuilderHubClient(string serverUrl, string username) {
            ServerUrl = serverUrl;
            Username = username;
        }

        public async Task ConnectAsync() {
            if (_hubConnection != null)
                throw new InvalidOperationException("Already connected to a hub");

            _hubConnection = new HubConnectionBuilder()
                .WithAutomaticReconnect()
                .WithUrl(ServerUrl)
                .Build();

            _hubConnection.On<UpdateMessage>("UpdateMessage", OnUpdateReceived);
            _hubConnection.Closed += HubConnection_Closed;
            _hubConnection.Reconnecting += HubConnection_Reconnecting;
            _hubConnection.Reconnected += HubConnection_Reconnected;

            await _hubConnection.StartAsync();
            await JoinProjectAsync(new(Username));
        }

        public async Task DisconnectAsync() {
            if (_hubConnection != null) {
                await _hubConnection.DisposeAsync();
                _hubConnection = null;
            }
        }

        private async Task JoinProjectAsync(JoinProjectMessage message) {
            ThrowIfNotConnected();
            if (HasJoinedProject) throw new InvalidOperationException("Already joined project");

            await _hubConnection!.InvokeAsync("JoinProject", message);
        }

        public async Task LeaveProjectAsync(LeaveProjectMessage message) {
            ThrowIfNotConnected();
            if (!HasJoinedProject) throw new InvalidOperationException("Not joined project");
            await _hubConnection!.InvokeAsync("LeaveProject", message);
        }

        private async Task HubConnection_Reconnected(string? arg) {
            await JoinProjectAsync(new(Username));
        }

        private Task HubConnection_Reconnecting(Exception? exception) {
            HasJoinedProject = false;
            IsSyncedWithServer = false;

            return Task.CompletedTask;
        }

        private Task HubConnection_Closed(Exception? exception) {
            HasJoinedProject = false;
            IsSyncedWithServer = false;

            return Task.CompletedTask;
        }

        private async Task OnUpdateReceived(UpdateMessage message) {
            if (UpdateReceived != null)
                await UpdateReceived.Invoke(message);
        }

        private void ThrowIfNotConnected() {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected to hub");
        }

        private void ThrowIfNotJoinedProject() {
            if (!HasJoinedProject)
                throw new InvalidOperationException("Not joined project");
        }

        public void Dispose() {
            if (!_disposed) {
                _ = DisconnectAsync();
                _disposed = true;
            }
        }
    }
}
