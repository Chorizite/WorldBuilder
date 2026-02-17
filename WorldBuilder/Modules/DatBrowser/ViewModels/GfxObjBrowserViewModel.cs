using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Lib.IO;
using DatReaderWriter.Types;
using DatReaderWriter;

using WorldBuilder.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class GfxObjBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.GfxObj> {
        public GfxObjBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.GfxObj, dats, settings, themeService) {
        }
    }
}
