namespace WorldBuilder.Shared.Hubs {
    public interface IWorldHubClient {
        Task DocumentEventReceived(byte[] data);
    }
}