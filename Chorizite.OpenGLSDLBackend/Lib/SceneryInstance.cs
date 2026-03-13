using Chorizite.Core.Lib;
using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Models;

namespace Chorizite.OpenGLSDLBackend.Lib {
    /// <summary>
    /// Lightweight data for a single placed scenery object.
    /// </summary>
    public struct SceneryInstance {
        /// <summary>GfxObj or Setup ID from DAT.</summary>
        public ulong ObjectId;

        /// <summary>Unique instance ID within the landblock.</summary>
        public ObjectId InstanceId;

        /// <summary>True for multi-part Setup objects, false for simple GfxObj.</summary>
        public bool IsSetup;

        /// <summary>True if this instance is a building.</summary>
        public bool IsBuilding;

        /// <summary>True if this is an interior cell connected directly to the landblock.</summary>
        public bool IsEntryCell;

        /// <summary>World-space position.</summary>
        public Vector3 WorldPosition;

        /// <summary>Local-space position (relative to landblock origin).</summary>
        public Vector3 LocalPosition;

        /// <summary>Rotation quaternion.</summary>
        public Quaternion Rotation;

        /// <summary>The current cell ID this instance is in (used for previewing moves between cells).</summary>
        public uint CurrentPreviewCellId;

        /// <summary>Scale (typically uniform).</summary>
        public Vector3 Scale;

        /// <summary>Pre-computed world transform matrix.</summary>
        public Matrix4x4 Transform;

        /// <summary>Local-space bounding box.</summary>
        public BoundingBox LocalBoundingBox;

        /// <summary>World-space bounding box.</summary>
        public BoundingBox BoundingBox;
    }

    /// <summary>
    /// Holds all instances for a single landblock, ready for rendering.
    /// Shared by both scenery and static object render managers.
    /// </summary>
    public class ObjectLandblock {
        /// <summary>Grid X coordinate of this landblock.</summary>
        public int GridX { get; set; }

        /// <summary>Grid Y coordinate of this landblock.</summary>
        public int GridY { get; set; }

        public object Lock { get; } = new();

        public List<SceneryInstance> Instances { get; set; } = new();

        /// <summary>
        /// Grouped bounding boxes for each EnvCell in this landblock.
        /// Key: CellID, Value: Composite bounding box of the cell and all its static objects.
        /// </summary>
        public Dictionary<uint, BoundingBox> EnvCellBounds { get; set; } = new();

        /// <summary>
        /// Set of EnvCell IDs in this landblock that have the SeenOutside flag.
        /// </summary>
        public HashSet<uint> SeenOutsideCells { get; set; } = new();

        public List<SceneryInstance>? PendingInstances { get; set; }

        /// <summary>
        /// Grouped bounding boxes for each EnvCell in this landblock (pending upload).
        /// </summary>
        public Dictionary<uint, BoundingBox>? PendingEnvCellBounds { get; set; }

        /// <summary>
        /// Set of EnvCell IDs in this landblock that have the SeenOutside flag (pending upload).
        /// </summary>
        public HashSet<uint>? PendingSeenOutsideCells { get; set; }

        /// <summary>
        /// Grouped transforms for each GfxObj part for static objects, for efficient instanced rendering.
        /// Key: GfxObjId, Value: List of transforms
        /// </summary>
        public Dictionary<ulong, List<InstanceData>> StaticPartGroups { get; set; } = new();

        /// <summary>
        /// Grouped transforms for each GfxObj part for buildings, for efficient instanced rendering.
        /// Key: GfxObjId, Value: List of transforms
        /// </summary>
        public Dictionary<ulong, List<InstanceData>> BuildingPartGroups { get; set; } = new();

        /// <summary>
        /// World-space bounding box of this landblock.
        /// </summary>
        public BoundingBox BoundingBox { get; set; }

        /// <summary>
        /// Whether instances (positions/bounding boxes) have been generated.
        /// Useful for scenery manager to know it can proceed with collision detection.
        /// </summary>
        /// <summary>
        /// Total bounding box covering all EnvCells in this landblock.
        /// </summary>
        public BoundingBox TotalEnvCellBounds { get; set; }

        /// <summary>
        /// Total bounding box covering all EnvCells in this landblock (pending upload).
        /// </summary>
        public BoundingBox PendingTotalEnvCellBounds { get; set; }

        public bool InstancesReady { get; set; }

        /// <summary>
        /// Whether mesh data for all instances has been prepared (CPU-side).
        /// </summary>
        public bool MeshDataReady { get; set; }

        /// <summary>
        /// Whether GPU resources have been uploaded.
        /// </summary>
        public bool GpuReady { get; set; }

        /// <summary>
        /// Whether this landblock is currently in the upload queue.
        /// </summary>
        public int IsQueuedForUpload;

        /// <summary>
        /// When set to 1, the pending upload is a transform-only update (e.g. drag preview).
        /// The upload path will skip buffer reallocation and mesh re-upload.
        /// </summary>
        public int IsTransformOnlyUpdate;

        // Optimized rendering data
        public int InstanceBufferOffset { get; set; } = -1;
        public int InstanceCount { get; set; }

        /// <summary>
        /// Pre-calculated draw commands and batch data for this landblock.
        /// Keyed by CullMode to allow grouped rendering.
        /// </summary>
        public Dictionary<DatReaderWriter.Enums.CullMode, List<LandblockMdiCommand>> MdiCommands { get; set; } = new();
    }
}
