using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using System.Numerics;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.Landscape.ViewModels;

public partial class PortalViewModel : SelectedObjectViewModelBase {
    public override ObjectType Type => ObjectType.Portal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CellIdHex))]
    private uint _cellIdVal;

    public override uint ObjectId => CellIdVal;

    [ObservableProperty] private PortalFlags _flags;
    [ObservableProperty] private ushort _polygonId;
    [ObservableProperty] private uint _otherCellId;
    [ObservableProperty] private ushort _otherPortalId;

    public string OtherCellIdHex => $"0x{OtherCellId:X4}";

    public PortalViewModel(ushort landblockId, uint cellId, ObjectId instanceId, Vector3 position, Vector3 localPosition, Quaternion rotation, IDatDatabase? cellDatabase) 
        : base(instanceId, landblockId, position, localPosition, rotation) {
        CellIdVal = cellId;

        if (cellDatabase != null && cellDatabase.TryGet<EnvCell>(cellId, out var envCell)) {
            var rawPortalIndex = instanceId.Index;
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
