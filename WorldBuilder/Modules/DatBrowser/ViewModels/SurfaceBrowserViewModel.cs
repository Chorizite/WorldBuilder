using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Lib.IO;
using DatReaderWriter.Types;
using DatReaderWriter;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class SurfaceBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.Surface> {
        public SurfaceBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.Surface, dats, settings, themeService) {
        }

        protected override void OnObjectLoaded(DatReaderWriter.DBObjs.Surface? obj) {
            if (obj != null && obj.Id == 0) {
                obj.Id = SelectedFileId;
            }
        }
    }
}
