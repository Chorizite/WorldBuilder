using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Shared.Services {
    public interface ISyncClient {
        Task ConnectAsync(CancellationToken ct);
        Task DisconnectAsync();
        IAsyncEnumerable<BaseCommand> ReceiveEventsAsync(CancellationToken ct);
        Task SendEventAsync(BaseCommand evt, CancellationToken ct);
        Task<ulong> GetServerTimeAsync(CancellationToken ct);
        Task<IReadOnlyList<BaseCommand>> GetEventsSinceAsync(ulong lastServerTimestamp, CancellationToken ct);
    }
}