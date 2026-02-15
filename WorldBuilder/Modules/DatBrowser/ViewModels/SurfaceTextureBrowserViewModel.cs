using CommunityToolkit.Mvvm.ComponentModel;
using System;
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
    public partial class SurfaceTextureBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.SurfaceTexture> {
        [ObservableProperty]
        private uint _previewFileId;

        [ObservableProperty]
        private IReadOnlyList<uint> _textures = Array.Empty<uint>();

        public SurfaceTextureBrowserViewModel(IDatReaderWriter dats) : base(DBObjType.SurfaceTexture, dats) {
        }

        protected override void OnObjectLoaded(DatReaderWriter.DBObjs.SurfaceTexture? obj) {
            if (obj != null) {
                Textures = obj.Textures.Select(x => x.DataId).ToList();
                PreviewFileId = Textures.FirstOrDefault();
            }
            else {
                Textures = Array.Empty<uint>();
                PreviewFileId = 0;
            }
        }
    }
}
