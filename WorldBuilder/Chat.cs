using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorldBuilder {
    internal class Chat {
        public Chat() {

            ChatHub = new HubConnectionBuilder()
                .WithUrl("http://localhost:5000/chat")
                .Build();

            ChatHub.Closed += async (error) => {
                Console.WriteLine("Connection closed");
                await Task.Delay(new Random().Next(0, 5) * 1000);
                await ChatHub.StartAsync();
            };

            ChatHub.On<string>("ReceiveMessage", (message) => {
                var newMessage = $"message: {message}";
                Console.WriteLine(newMessage);
            });

            ChatHub.StartAsync();
        }

        public HubConnection ChatHub { get; }
    }
}
