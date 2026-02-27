using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Modules.Landscape.Models;
using System.Numerics;

namespace WorldBuilder.Modules.Landscape.ViewModels;

public partial class PortalViewModel : ViewModelBase, ISelectedObjectInfo {
    public InspectorSelectionType Type => InspectorSelectionType.Portal;
    public uint ObjectId => CellId;
    public ulong InstanceId => PortalIndex;
    public ushort SecondaryId => InstanceIdConstants.GetSecondaryId(InstanceId);
    public int VertexX => 0;
    public int VertexY => 0;

    [ObservableProperty] private uint _landblockId;
    [ObservableProperty] private uint _cellId;
    [ObservableProperty] private ulong _portalIndex;
    [ObservableProperty] private Vector3 _position;
    [ObservableProperty] private Vector3 _localPosition;
    [ObservableProperty] private Quaternion _rotation;

    public string CellIdHex => $"0x{CellId:X8}";
    public string LandblockIdHex => $"0x{LandblockId:X8}";
    public string InstanceIdHex => $"0x{InstanceId:X16}";

    [ObservableProperty] private PortalFlags _flags;
    [ObservableProperty] private ushort _polygonId;
    [ObservableProperty] private ushort _otherCellId;
    [ObservableProperty] private ushort _otherPortalId;

    public string OtherCellIdHex => $"0x{OtherCellId:X4}";

    public PortalViewModel(uint landblockId, uint cellId, ulong portalIndex, Vector3 position, Vector3 localPosition, Quaternion rotation, IDatReaderWriter dats, IPortalService portalService) {
        LandblockId = landblockId;
        CellId = cellId;
        PortalIndex = portalIndex;
        Position = position;
        LocalPosition = localPosition;
        Rotation = rotation;

        var regionId = (landblockId >> 16) & 0xFFFF;
        var portal = portalService.GetPortal(regionId, landblockId, cellId, InstanceIdConstants.GetRawId(portalIndex));
        
        // Note: PortalService currently only returns geometry data. 
        // We'll keep the Dat lookup for now to get flags and other properties.
        if (dats.CellRegions.TryGetValue(regionId, out var cellDb)) {
            if (cellDb.TryGet<EnvCell>(cellId, out var envCell)) {
                var rawPortalIndex = InstanceIdConstants.GetRawId(portalIndex);
                if (rawPortalIndex < (uint)envCell.CellPortals.Count) {
                    var dbPortal = envCell.CellPortals[(int)rawPortalIndex];
                    Flags = dbPortal.Flags;
                    PolygonId = dbPortal.PolygonId;
                    OtherCellId = dbPortal.OtherCellId;
                    OtherPortalId = dbPortal.OtherPortalId;
                }
            }
        }
    }
}
