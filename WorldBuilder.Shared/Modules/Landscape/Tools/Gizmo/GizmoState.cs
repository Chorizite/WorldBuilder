using System.Numerics;

namespace WorldBuilder.Shared.Modules.Landscape.Tools.Gizmo
{
    /// <summary>
    /// The active gizmo manipulation mode.
    /// </summary>
    public enum GizmoMode
    {
        Translate,
        Rotate
    }

    /// <summary>
    /// Identifies a component of the gizmo that can be interacted with.
    /// </summary>
    public enum GizmoComponent
    {
        None,
        AxisX,
        AxisY,
        AxisZ,
        Center,
        RingYaw,   // Rotation around Z axis (XY plane)
        RingPitch, // Rotation around X axis (YZ plane)
        RingRoll   // Rotation around Y axis (XZ plane)
    }

    /// <summary>
    /// Holds the current state of the manipulation gizmo.
    /// </summary>
    public class GizmoState
    {
        /// <summary>The current gizmo mode (translate or rotate).</summary>
        public GizmoMode Mode { get; set; } = GizmoMode.Translate;

        /// <summary>World-space center of the gizmo.</summary>
        public Vector3 Position { get; set; }

        /// <summary>Current rotation of the selected object.</summary>
        public Quaternion Rotation { get; set; } = Quaternion.Identity;

        /// <summary>Scale factor for gizmo rendering, computed from the object's bounding box.</summary>
        public float Size { get; set; } = 5f;

        /// <summary>The gizmo component currently under the mouse cursor.</summary>
        public GizmoComponent HoveredComponent { get; set; } = GizmoComponent.None;

        /// <summary>The gizmo component currently being dragged.</summary>
        public GizmoComponent ActiveComponent { get; set; } = GizmoComponent.None;

        /// <summary>Whether a drag operation is in progress.</summary>
        public bool IsDragging { get; set; }

        /// <summary>The selected object's landblock ID.</summary>
        public uint LandblockId { get; set; }

        /// <summary>The selected object's instance ID.</summary>
        public ulong InstanceId { get; set; }

        /// <summary>The selected object's setup/model ID.</summary>
        public uint ObjectId { get; set; }

        /// <summary>The selected object's local position (relative to landblock).</summary>
        public Vector3 LocalPosition { get; set; }

        /// <summary>The selected object's layer ID.</summary>
        public string LayerId { get; set; } = string.Empty;
    }
}
