using WorldBuilder.Shared.Messages;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WorldBuilder.Shared {
    public interface IWorldBuilderHubClient : IDisposable {
        event Func<UpdateMessage, Task> UpdateReceived;

        bool IsConnected { get; }
        bool HasJoinedProject { get; }
        bool IsSyncedWithServer { get; }

        Task ConnectAsync();
        Task DisconnectAsync();
        //Task JoinProjectAsync(JoinProjectMessage message);
        //Task LeaveProjectAsync(LeaveProjectMessage message);
    }
}
