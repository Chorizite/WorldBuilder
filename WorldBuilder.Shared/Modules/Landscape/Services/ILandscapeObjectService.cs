using System;
using System.Numerics;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;

namespace WorldBuilder.Shared.Modules.Landscape.Services;

/// <summary>
/// Service for managing landscape objects, including movement, selection, and validation.
/// </summary>
public interface ILandscapeObjectService {
    /// <summary>
    /// Gets or sets the currently selected object hit.
    /// </summary>
    SceneRaycastHit SelectedHit { get; set; }

    /// <summary>
    /// Event fired when the selected object hit changes.
    /// </summary>
    event EventHandler<SceneRaycastHit>? SelectionChanged;

    /// <summary>
    /// Computes the world position for a given landblock and local position.
    /// </summary>
    Vector3 ComputeWorldPosition(ITerrainInfo region, ushort landblockId, Vector3 localPosition);

    /// <summary>
    /// Computes the landblock ID for a given world position.
    /// </summary>
    ushort ComputeLandblockId(ITerrainInfo region, Vector3 worldPosition);

    /// <summary>
    /// Gets the layer ID for a specific static object instance.
    /// </summary>
    string? GetStaticObjectLayerId(LandscapeDocument doc, ushort landblockId, ObjectId instanceId);

    /// <summary>
    /// Moves a static object to a new position and rotation, with optional cell assignment.
    /// Handles sticky cell logic and command creation.
    /// </summary>
    Task MoveStaticObjectAsync(LandscapeDocument doc, CommandHistory commandHistory, string layerId, ushort oldLandblockId, ushort newLandblockId, StaticObject oldObject, StaticObject newObject);

    /// <summary>
    /// Determines the best cell ID for a given world position, considering an optional starting cell context.
    /// </summary>
    Task<uint?> ResolveCellIdAsync(LandscapeDocument doc, Vector3 worldPos, uint? startCellId = null);
}
