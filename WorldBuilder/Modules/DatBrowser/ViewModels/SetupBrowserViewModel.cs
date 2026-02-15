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

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class SetupBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.Setup> {
        public SetupBrowserViewModel(IDatReaderWriter dats) : base(DBObjType.Setup, dats) {
        }
    }
}
