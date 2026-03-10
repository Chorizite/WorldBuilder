using Microsoft.Extensions.DependencyInjection;
using WorldBuilder.Modules.DatBrowser.ViewModels;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.Factories {
    public class DatBrowserViewModelFactory : IDatBrowserViewModelFactory {
        private readonly IServiceProvider _serviceProvider;

        public DatBrowserViewModelFactory(IServiceProvider serviceProvider) {
            _serviceProvider = serviceProvider;
        }

        public SetupBrowserViewModel CreateSetupBrowser() {
            return _serviceProvider.GetRequiredService<SetupBrowserViewModel>();
        }

        public GfxObjBrowserViewModel CreateGfxObjBrowser() {
            return _serviceProvider.GetRequiredService<GfxObjBrowserViewModel>();
        }

        public SurfaceTextureBrowserViewModel CreateSurfaceTextureBrowser() {
            return _serviceProvider.GetRequiredService<SurfaceTextureBrowserViewModel>();
        }

        public RenderSurfaceBrowserViewModel CreateRenderSurfaceBrowser() {
            return _serviceProvider.GetRequiredService<RenderSurfaceBrowserViewModel>();
        }

        public SurfaceBrowserViewModel CreateSurfaceBrowser() {
            return _serviceProvider.GetRequiredService<SurfaceBrowserViewModel>();
        }

        public EnvCellBrowserViewModel CreateEnvCellBrowser() {
            return _serviceProvider.GetRequiredService<EnvCellBrowserViewModel>();
        }
    }
}
