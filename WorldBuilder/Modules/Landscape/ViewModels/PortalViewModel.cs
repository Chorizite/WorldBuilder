using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using System.Numerics;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.Landscape.ViewModels;

public partial class PortalViewModel : SelectedObjectViewModelBase {
    public override InspectorSelectionType Type => InspectorSelectionType.Portal;

    public override uint ObjectId => CellId ?? 0;

    [ObservableProperty] private PortalFlags _flags;
    [ObservableProperty] private ushort _polygonId;
    [ObservableProperty] private ushort _otherCellId;
    [ObservableProperty] private ushort _otherPortalId;

    public string OtherCellIdHex => $"0x{OtherCellId:X4}";

    public PortalViewModel(ushort landblockId, uint? cellId, ulong instanceId, Vector3 position, Vector3 localPosition, Quaternion rotation, IDatDatabase? cellDatabase) 
        : base(instanceId, landblockId, position, localPosition, rotation) {
        CellId = cellId;

        if (cellId.HasValue && cellDatabase != null && cellDatabase.TryGet<EnvCell>(cellId.Value, out var envCell)) {
            var rawPortalIndex = InstanceIdConstants.GetRawId(instanceId);
            if (rawPortalIndex < (uint)envCell.CellPortals.Count) {
                var dbPortal = envCell.CellPortals[(int)rawPortalIndex];
                this.Flags = dbPortal.Flags;
                this.PolygonId = dbPortal.PolygonId;
                this.OtherCellId = dbPortal.OtherCellId;
                this.OtherPortalId = dbPortal.OtherPortalId;
            }
        }
    }
}
