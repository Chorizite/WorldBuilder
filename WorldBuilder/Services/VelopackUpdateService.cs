using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace WorldBuilder.Services;

public class VelopackUpdateService : IUpdateService {
    private readonly ILogger<VelopackUpdateService> _logger;
    private const string RepoUrl = "https://github.com/Chorizite/WorldBuilder";

    public VelopackUpdateService(ILogger<VelopackUpdateService> logger) {
        _logger = logger;
    }

    public async Task<UpdateInfo?> CheckForUpdatesAsync() {
        try {
            var mgr = new UpdateManager(new GithubSource(RepoUrl, null, false));
            if (!mgr.IsInstalled) {
                _logger.LogInformation("Velopack not installed. Skipping update check.");
                return null;
            }

            var updateInfo = await mgr.CheckForUpdatesAsync();
            return updateInfo;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to check for updates.");
            return null;
        }
    }

    public async Task DownloadAndInstallUpdateAsync(UpdateInfo updateInfo) {
        try {
            var mgr = new UpdateManager(new GithubSource(RepoUrl, null, false));
            if (!mgr.IsInstalled) return;

            await mgr.DownloadUpdatesAsync(updateInfo);
            mgr.ApplyUpdatesAndRestart(updateInfo);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to download/install update.");
        }
    }
}