using System.Threading.Tasks;
using Velopack;

namespace WorldBuilder.Services;

public interface IUpdateService
{
    Task<UpdateInfo?> CheckForUpdatesAsync();
    Task DownloadAndInstallUpdateAsync(UpdateInfo updateInfo);
}
