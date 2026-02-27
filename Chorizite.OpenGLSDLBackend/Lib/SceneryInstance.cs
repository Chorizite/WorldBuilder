using Chorizite.Core.Lib;
using System.Collections.Generic;
using System.Numerics;

namespace Chorizite.OpenGLSDLBackend.Lib {
    /// <summary>
    /// Lightweight data for a single placed scenery object.
    /// </summary>
    public struct SceneryInstance {
        /// <summary>GfxObj or Setup ID from DAT.</summary>
        public ulong ObjectId;

        /// <summary>Unique instance ID within the landblock.</summary>
        public ulong InstanceId;

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

        public List<SceneryInstance>? PendingInstances { get; set; }

        /// <summary>
        /// Grouped bounding boxes for each EnvCell in this landblock (pending upload).
        /// </summary>
        public Dictionary<uint, BoundingBox>? PendingEnvCellBounds { get; set; }

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
        /// Whether instances (positions/bounding boxes) have been generated.
        /// Useful for scenery manager to know it can proceed with collision detection.
        /// </summary>
        public bool InstancesReady { get; set; }

        /// <summary>
        /// Whether mesh data for all instances has been prepared (CPU-side).
        /// </summary>
        public bool MeshDataReady { get; set; }

        /// <summary>
        /// Whether GPU resources have been uploaded.
        /// </summary>
        public bool GpuReady { get; set; }
    }
}
