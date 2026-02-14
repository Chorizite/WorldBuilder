using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.DBObjs;
using DatReaderWriter;
using DatReaderWriter.Enums;
using System.Collections.Generic;
using System.Linq;
using WorldBuilder.ViewModels;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class SurfaceTextureOverviewViewModel : ViewModelBase {
        public SurfaceTexture SurfaceTexture { get; }
        public IDatReaderWriter Dats { get; }

        public TextureType Type => SurfaceTexture.Type;

        public List<ReflectionNodeViewModel> Textures { get; }

        [ObservableProperty]
        private ReflectionNodeViewModel? _selectedTexture;

        public uint SelectedTextureId {
            get => SelectedTexture?.DataId ?? 0;
            set {
                var node = Textures.FirstOrDefault(x => x.DataId == value);
                if (node != null) {
                    SelectedTexture = node;
                }
            }
        }

        public SurfaceTextureOverviewViewModel(SurfaceTexture surfaceTexture, IDatReaderWriter dats) {
            SurfaceTexture = surfaceTexture;
            Dats = dats;

            Textures = surfaceTexture.Textures.Select((id, index) => {
                var type = dats.TypeFromId(id.DataId);
                var node = new ReflectionNodeViewModel($"[{index}]", $"0x{id.DataId:X8}", type.ToString());
                node.DataId = id.DataId;
                node.Dats = dats;
                node.DbType = type;
                return node;
            }).ToList();

            SelectedTexture = Textures.FirstOrDefault();
        }
    }
}
