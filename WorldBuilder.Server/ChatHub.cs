using System;
using Microsoft.AspNetCore.SignalR;

namespace WorldBuilder.Server {
    public class ChatHub : Hub {

        public ChatHub() {
        }

        public async Task SendMessage(string user, string message) {
            await Clients.All.SendAsync("ReceiveMessage", user, message);
        }
    }
}
