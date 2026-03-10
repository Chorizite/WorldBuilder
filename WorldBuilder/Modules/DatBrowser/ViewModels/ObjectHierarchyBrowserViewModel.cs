using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class ObjectHierarchyBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.ObjectHierarchy> {
        public ObjectHierarchyBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.ObjectHierarchy, dats, settings, themeService) {
        }
    }
}
