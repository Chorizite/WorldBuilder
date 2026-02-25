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
using System.Numerics;

namespace WorldBuilder.Modules.Landscape.ViewModels;

public partial class PortalViewModel : ViewModelBase, ISelectedObjectInfo {
    public InspectorSelectionType Type => InspectorSelectionType.Portal;
    public uint ObjectId => CellId;
    public uint InstanceId => PortalIndex;
    public int VertexX => 0;
    public int VertexY => 0;

    [ObservableProperty] private uint _landblockId;
    [ObservableProperty] private uint _cellId;
    [ObservableProperty] private uint _portalIndex;
    [ObservableProperty] private Vector3 _position;
    [ObservableProperty] private Quaternion _rotation;

    public string CellIdHex => $"0x{CellId:X8}";
    public string LandblockIdHex => $"0x{LandblockId:X8}";

    [ObservableProperty] private PortalFlags _flags;
    [ObservableProperty] private ushort _polygonId;
    [ObservableProperty] private ushort _otherCellId;
    [ObservableProperty] private ushort _otherPortalId;

    public string OtherCellIdHex => $"0x{OtherCellId:X4}";

    public PortalViewModel(uint landblockId, uint cellId, uint portalIndex, IDatReaderWriter dats, IPortalService portalService) {
        LandblockId = landblockId;
        CellId = cellId;
        PortalIndex = portalIndex;

        var regionId = (landblockId >> 16) & 0xFFFF;
        var portal = portalService.GetPortal(regionId, landblockId, cellId, portalIndex);
        
        // Note: PortalService currently only returns geometry data. 
        // We'll keep the Dat lookup for now to get flags and other properties.
        if (dats.CellRegions.TryGetValue(regionId, out var cellDb)) {
            if (cellDb.TryGet<EnvCell>(cellId, out var envCell)) {
                if (portalIndex < envCell.CellPortals.Count) {
                    var dbPortal = envCell.CellPortals[(int)portalIndex];
                    Flags = dbPortal.Flags;
                    PolygonId = dbPortal.PolygonId;
                    OtherCellId = dbPortal.OtherCellId;
                    OtherPortalId = dbPortal.OtherPortalId;
                }
            }
        }
    }
}
