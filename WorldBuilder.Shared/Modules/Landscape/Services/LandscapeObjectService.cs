using System;
using System.Numerics;
using System.Threading.Tasks;
using System.Linq;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Services;
using WorldBuilder.Shared.Modules.Landscape.Tools;

namespace WorldBuilder.Shared.Modules.Landscape.Services;

public class LandscapeObjectService : ILandscapeObjectService {
    private readonly IWorldCoordinateService _worldCoords;
    private SceneRaycastHit _selectedHit;

    public LandscapeObjectService(IWorldCoordinateService worldCoords) {
        _worldCoords = worldCoords;
    }

    public SceneRaycastHit SelectedHit {
        get => _selectedHit;
        set {
            if (!Equals(_selectedHit, value)) {
                _selectedHit = value;
                SelectionChanged?.Invoke(this, value);
            }
        }
    }

    public event EventHandler<SceneRaycastHit>? SelectionChanged;

    public Vector3 ComputeWorldPosition(ITerrainInfo region, ushort landblockId, Vector3 localPosition) {
        uint lbX = (uint)(landblockId >> 8) & 0xFF;
        uint lbY = (uint)(landblockId & 0xFF);
        var origin = new Vector3(lbX * region.LandblockSizeInUnits + region.MapOffset.X,
                                 lbY * region.LandblockSizeInUnits + region.MapOffset.Y, 0);
        return origin + localPosition;
    }

    public ushort ComputeLandblockId(ITerrainInfo region, Vector3 worldPosition) {
        var offset = region.MapOffset;
        var lbSize = region.LandblockSizeInUnits;
        int lbX = (int)Math.Floor((worldPosition.X - offset.X) / lbSize);
        int lbY = (int)Math.Floor((worldPosition.Y - offset.Y) / lbSize);
        return (ushort)((lbX << 8) | lbY);
    }

    private ushort StandardizeLandblockId(uint landblockId) {
        if ((landblockId & 0xFFFF) >= 0xFFFE) {
            return (ushort)(landblockId >> 16);
        }
        return (ushort)((landblockId >> 16) & 0xFFFF);
    }

    public string? GetStaticObjectLayerId(LandscapeDocument doc, ushort landblockId, ObjectId instanceId) {
        var type = instanceId.Type;

        if (type == ObjectType.EnvCellStaticObject) {
            var cellId = instanceId.Context;
            var mergedCell = doc.GetMergedEnvCell(cellId);
            if (mergedCell.StaticObjects != null && mergedCell.StaticObjects.TryGetValue(instanceId, out var obj)) {
                return obj.LayerId;
            }
            return null;
        }

        if (type == ObjectType.EnvCell) {
            var cellId = instanceId.Context;
            var mergedCell = doc.GetMergedEnvCell(cellId);
            return mergedCell.LayerId;
        }

        if (type == ObjectType.Portal || type == ObjectType.Scenery) {
            return doc.BaseLayerId ?? string.Empty;
        }

        var merged = doc.GetMergedLandblock(landblockId);
        if (merged.StaticObjects.TryGetValue(instanceId, out var staticObj)) {
            return staticObj.LayerId;
        }
        if (merged.Buildings.TryGetValue(instanceId, out var buildingObj)) {
            return buildingObj.LayerId;
        }
        return null;
    }

    public async Task MoveStaticObjectAsync(LandscapeDocument doc, CommandHistory commandHistory, string layerId, ushort oldLandblockId, ushort newLandblockId, StaticObject oldObject, StaticObject newObject) {
        var command = new MoveStaticObjectCommand(doc, null, layerId, oldLandblockId, newLandblockId, oldObject, newObject);
        commandHistory.Execute(command);
    }

    public async Task<uint?> ResolveCellIdAsync(LandscapeDocument doc, Vector3 worldPos, uint? startCellId = null) {
        var finalCellId = await doc.GetEnvCellAtAsync(worldPos);
        return finalCellId != 0 ? finalCellId : null;
    }

    private bool Equals(SceneRaycastHit a, SceneRaycastHit b) {
        return a.Type == b.Type && a.LandblockId == b.LandblockId && a.InstanceId == b.InstanceId && a.ObjectId == b.ObjectId;
    }
}
