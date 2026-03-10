using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class ChatPoseTableBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.ChatPoseTable> {
        public ChatPoseTableBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.ChatPoseTable, dats, settings, themeService) {
        }
    }
}
