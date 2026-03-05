using System.Numerics;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools.Gizmo {


    /// <summary>
    /// Identifies a component of the gizmo that can be interacted with.
    /// </summary>
    public enum GizmoComponent {
        None,
        AxisX,
        AxisY,
        AxisZ,
        Center,
        PlaneXY,
        PlaneXZ,
        PlaneYZ,
        RingYaw,   // Rotation around Z axis (XY plane)
        RingPitch, // Rotation around X axis (YZ plane)
        RingRoll   // Rotation around Y axis (XZ plane)
    }

    /// <summary>
    /// Holds the current state of the manipulation gizmo.
    /// </summary>
    public class GizmoState {
        /// <summary>Whether the gizmo uses local object space instead of global space.</summary>
        public bool IsLocalSpace { get; set; }

        /// <summary>World-space center of the gizmo.</summary>
        public Vector3 Position { get; set; }

        /// <summary>Current rotation of the selected object.</summary>
        public Quaternion Rotation { get; set; } = Quaternion.Identity;

        /// <summary>World-space camera position for distance-based scaling.</summary>
        public Vector3 CameraPosition { get; set; }

        /// <summary>Calculates the screen-relative size of the gizmo based on distance to the camera.</summary>
        public float GetScreenSize() {
            float distance = Vector3.Distance(CameraPosition, Position);
            float baseScale = 0.2f;
            return MathF.Max(0.5f, distance * baseScale);
        }

        /// <summary>The gizmo component currently under the mouse cursor.</summary>
        public GizmoComponent HoveredComponent { get; set; } = GizmoComponent.None;

        /// <summary>The gizmo component currently being dragged.</summary>
        public GizmoComponent ActiveComponent { get; set; } = GizmoComponent.None;

        /// <summary>Whether a drag operation is in progress.</summary>
        public bool IsDragging { get; set; }

        public bool IsRotating { get; set; }
        public Vector3 RotationAxis { get; set; }
        public Vector3 RotationStartAxis { get; set; }
        public float RotationAngle { get; set; }

        /// <summary>The selected object's selection type.</summary>
        public InspectorSelectionType SelectionType { get; set; }

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
