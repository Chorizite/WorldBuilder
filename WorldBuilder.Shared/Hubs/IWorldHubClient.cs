namespace WorldBuilder.Shared.Hubs {
    /// <summary>
    /// The interface for the World Hub client.
    /// </summary>
    public interface IWorldHubClient {
        /// <summary>
        /// Called when a document event is received from the server.
        /// </summary>
        /// <param name="data">The serialized event data.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task DocumentEventReceived(byte[] data);
    }
}