using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.DBObjs;
using System.Numerics;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.Landscape.ViewModels;

public partial class EnvCellViewModel : SelectedObjectViewModelBase {
    public override InspectorSelectionType Type => InspectorSelectionType.EnvCell;

    [ObservableProperty] private uint _environmentId;

    public override uint ObjectId => CellId ?? 0;

    public EnvCellViewModel(uint cellId, ulong instanceId, ushort landblockId, Vector3 position, Vector3 localPosition, Quaternion rotation, IDatDatabase? cellDatabase) 
        : base(instanceId, landblockId, position, localPosition, rotation) {
        CellId = cellId;

        if (cellDatabase != null && cellDatabase.TryGet<EnvCell>(cellId, out var envCell)) {
            this.EnvironmentId = 0x0D000000u | (uint)envCell.EnvironmentId;
        }
    }
}
