using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorldBuilder.Shared.Hubs {
    /// <summary>
    /// The interface for the World Hub.
    /// </summary>
    public interface IWorldHub {
        /// <summary>
        /// Sends a document event to the server.
        /// </summary>
        /// <param name="data">The serialized event data.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task ReceiveDocumentEvent(byte[] data);

        /// <summary>
        /// Gets the current server time.
        /// </summary>
        /// <returns>The current server time in ulong format.</returns>
        Task<ulong> GetServerTime();

        /// <summary>
        /// Gets all events since a specific server timestamp.
        /// </summary>
        /// <param name="lastServerTimestamp">The timestamp to get events from.</param>
        /// <returns>A collection of serialized event data.</returns>
        Task<byte[][]> GetEventsSince(ulong lastServerTimestamp);
    }
}