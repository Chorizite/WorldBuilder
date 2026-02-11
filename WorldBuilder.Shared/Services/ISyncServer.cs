using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Shared.Services {
    public interface ISyncServer {
        Task BroadcastEventAsync(BaseCommand evt, CancellationToken ct);
        Task<ulong> GetServerTimeAsync();
    }
}