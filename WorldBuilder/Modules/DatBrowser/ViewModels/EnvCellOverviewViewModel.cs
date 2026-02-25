using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.DBObjs;
using DatReaderWriter;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using System.Collections.Generic;
using System.Linq;
using WorldBuilder.ViewModels;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels
{
    public partial class EnvCellOverviewViewModel : ViewModelBase
    {
        public EnvCell EnvCell { get; }
        public IDatReaderWriter Dats { get; }

        public EnvCellFlags Flags => EnvCell.Flags;
        public string Position => EnvCell.Position?.ToString() ?? "";

        public ReflectionNodeViewModel EnvironmentIdNode { get; }
        public List<ReflectionNodeViewModel> StaticObjects { get; }
        public List<ReflectionNodeViewModel> Surfaces { get; }

        [ObservableProperty]
        private ReflectionNodeViewModel? _selectedItem;

        public EnvCellOverviewViewModel(EnvCell envCell, IDatReaderWriter dats)
        {
            EnvCell = envCell;
            Dats = dats;

            uint fullEnvId = 0x0D000000 | (uint)envCell.EnvironmentId;
            var envResolutions = dats.ResolveId(fullEnvId).ToList();
            var envType = envResolutions.FirstOrDefault()?.Type ?? DBObjType.Unknown;
            EnvironmentIdNode = new ReflectionNodeViewModel("Environment", $"0x{fullEnvId:X8}", envType.ToString())
            {
                DataId = fullEnvId,
                Dats = dats,
                DbType = envType
            };

            StaticObjects = envCell.StaticObjects.Select((stab, index) =>
            {
                var resolutions = dats.ResolveId(stab.Id).ToList();
                var type = resolutions.FirstOrDefault()?.Type ?? DBObjType.Unknown;
                var node = new ReflectionNodeViewModel($"[{index}]", $"0x{stab.Id:X8}", type.ToString());
                node.DataId = stab.Id;
                node.Dats = dats;
                node.DbType = type;
                return node;
            }).ToList();

            Surfaces = envCell.Surfaces.Select((surfaceId, index) =>
            {
                uint fullId = 0x08000000 | (uint)surfaceId;
                var resolutions = dats.ResolveId(fullId).ToList();
                var type = resolutions.FirstOrDefault()?.Type ?? DBObjType.Unknown;
                var node = new ReflectionNodeViewModel($"[{index}]", $"0x{fullId:X8}", type.ToString());
                node.DataId = fullId;
                node.Dats = dats;
                node.DbType = type;
                return node;
            }).ToList();
        }
    }
}
