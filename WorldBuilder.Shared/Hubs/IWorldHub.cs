using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorldBuilder.Shared.Hubs {
    public interface IWorldHub {
        Task ReceiveDocumentEvent(byte[] data);
        Task<ulong> GetServerTime();
        Task<byte[][]> GetEventsSince(ulong lastServerTimestamp);
    }
}