using Chorizite.Core.Lib;
using System.Numerics;

namespace Chorizite.OpenGLSDLBackend.Lib {
    /// <summary>
    /// Lightweight data for a single placed scenery object.
    /// </summary>
    public struct SceneryInstance {
        /// <summary>GfxObj or Setup ID from DAT.</summary>
        public uint ObjectId;

        /// <summary>True for multi-part Setup objects, false for simple GfxObj.</summary>
        public bool IsSetup;

        /// <summary>World-space position.</summary>
        public Vector3 WorldPosition;

        /// <summary>Rotation quaternion.</summary>
        public Quaternion Rotation;

        /// <summary>Scale (typically uniform).</summary>
        public Vector3 Scale;

        /// <summary>Pre-computed world transform matrix.</summary>
        public Matrix4x4 Transform;

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

        public List<SceneryInstance> Instances { get; set; } = new();

        public List<SceneryInstance>? PendingInstances { get; set; }

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
