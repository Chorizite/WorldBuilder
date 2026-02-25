using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.DBObjs;
using DatReaderWriter;
using DatReaderWriter.Enums;
using System.Collections.Generic;
using System.Linq;
using WorldBuilder.ViewModels;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class EnvCellOverviewViewModel : ViewModelBase {
        public EnvCell EnvCell { get; }
        public IDatReaderWriter Dats { get; }

        public EnvCellFlags Flags => EnvCell.Flags;
        public string Position => EnvCell.Position?.ToString() ?? "";

        public ReflectionNodeViewModel EnvironmentIdNode { get; }
        public List<ReflectionNodeViewModel> StaticObjects { get; }
        public List<ReflectionNodeViewModel> Surfaces { get; }

        [ObservableProperty]
        private ReflectionNodeViewModel? _selectedItem;

        public EnvCellOverviewViewModel(EnvCell envCell, IDatReaderWriter dats) {
            EnvCell = envCell;
            Dats = dats;

            uint fullEnvId = 0x0D000000 | (uint)envCell.EnvironmentId;
            EnvironmentIdNode = ReflectionNodeViewModel.CreateFromDataId("Environment", fullEnvId, dats);

            StaticObjects = envCell.StaticObjects.Select((stab, index) =>
                ReflectionNodeViewModel.CreateFromDataId($"[{index}]", stab.Id, dats)
            ).ToList();

            Surfaces = envCell.Surfaces.Select((surfaceId, index) => {
                uint fullId = 0x08000000 | (uint)surfaceId;
                return ReflectionNodeViewModel.CreateFromDataId($"[{index}]", fullId, dats);
            }).ToList();
        }
    }
}
