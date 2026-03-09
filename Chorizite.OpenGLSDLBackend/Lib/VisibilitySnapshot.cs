using System.Collections.Generic;

namespace Chorizite.OpenGLSDLBackend.Lib {
    /// <summary>
    /// An immutable (after creation) snapshot of visibility state for a single frame.
    /// Used for atomic swapping between preparation and rendering threads.
    /// </summary>
    public class VisibilitySnapshot {
        /// <summary>Landblocks fully or partially inside the frustum (fast path).</summary>
        public List<ObjectLandblock> VisibleLandblocks { get; init; } = new();

        /// <summary>Landblocks intersecting the frustum (currently unused but reserved).</summary>
        public List<ObjectLandblock> IntersectingLandblocks { get; init; } = new();

        /// <summary>
        /// Grouped instance data for objects that need dynamic buffer updates (slow path).
        /// Key: GfxObjId, Value: List of transforms
        /// </summary>
        public Dictionary<ulong, List<InstanceData>> VisibleGroups { get; init; } = new();

        /// <summary>List of GfxObjIds in VisibleGroups for stable iteration.</summary>
        public List<ulong> VisibleGfxObjIds { get; init; } = new();

        /// <summary>
        /// For EnvCellRenderManager: Grouped instance data by CellId.
        /// Key: CellId, Value: { GfxObjId: List<InstanceData> }
        /// </summary>
        public Dictionary<uint, Dictionary<ulong, List<InstanceData>>> BatchedByCell { get; init; } = new();

        /// <summary>The pool index at the end of preparation, used to reset the pool in Render.</summary>
        public int PostPreparePoolIndex { get; init; }

        /// <summary>Whether this snapshot contains any visible objects.</summary>
        public bool IsEmpty => VisibleLandblocks.Count == 0 && VisibleGfxObjIds.Count == 0;
    }
}
