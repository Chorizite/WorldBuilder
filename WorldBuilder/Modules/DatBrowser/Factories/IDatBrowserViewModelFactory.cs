using WorldBuilder.Modules.DatBrowser.ViewModels;

namespace WorldBuilder.Modules.DatBrowser.Factories {
    public interface IDatBrowserViewModelFactory {
        SetupBrowserViewModel CreateSetupBrowser();
        GfxObjBrowserViewModel CreateGfxObjBrowser();
        SurfaceTextureBrowserViewModel CreateSurfaceTextureBrowser();
        RenderSurfaceBrowserViewModel CreateRenderSurfaceBrowser();
        SurfaceBrowserViewModel CreateSurfaceBrowser();
        EnvCellBrowserViewModel CreateEnvCellBrowser();
    }
}
