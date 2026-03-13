using System;
using System.Numerics;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools.Gizmo {

    /// <summary>
    /// The manipulation mode for the gizmo.
    /// </summary>
    public enum GizmoMode {
        Translate,
        Rotate,
        Both
    }

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
    /// Centralized configuration for gizmo sizing and proportions.
    /// All values are multipliers relative to the base 'size' calculated in GetScreenSize().
    /// </summary>
    public static class GizmoConfig {
        // Overall Scaling
        public const float MinPixelSize = 60f;      // Minimum size in pixels on screen
        public const float MaxPixelSize = 350f;     // Maximum size in pixels on screen
        public const float MaxViewportRatio = 0.25f; // Never more than 35% of the smaller viewport dimension
        public const float DefaultPixelSize = 160f; // Default size in pixels if no object is selected
        public const float ObjectSizeRatio = 0.5f;    // Target 50% of the object's largest dimension

        // Translation Axis
        public const float TranslationCylinderRadius = 0.015f;
        public const float TranslationConeRadius = 0.06f;
        public const float TranslationConeLength = 0.20f;
        public const float TranslationHitPadding = 1.5f; // Multiplier for the cylinder/cone radius for hit testing

        // Rotation Rings
        public const float RotationTubeRadius = 0.015f;
        public const float RotationHitPadding = 3.5f; // Multiplier for the tube radius for hit testing

        // Planes
        public const float PlaneOffset = 0.3f;
        public const float PlaneSize = 0.2f;
        public const float PlaneHitPadding = 1.5f; // Multiplier for the plane size for hit testing

        // Center
        public const float CenterBoxSize = 0.15f;
        public const float CenterHitPadding = 2.5f; // Multiplier for the box size for hit testing
    }

    /// <summary>
    /// Holds the current state of the manipulation gizmo.
    /// </summary>
    public class GizmoState {
        /// <summary>The current manipulation mode.</summary>
        public GizmoMode Mode { get; set; } = GizmoMode.Translate;

        /// <summary>Whether the gizmo uses local object space instead of global space.</summary>
        public bool IsLocalSpace { get; set; }

        /// <summary>World-space center of the gizmo.</summary>
        public Vector3 Position { get; set; }

        /// <summary>Current rotation of the selected object.</summary>
        public Quaternion Rotation { get; set; } = Quaternion.Identity;

        /// <summary>World-space camera position for distance-based scaling.</summary>
        public Vector3 CameraPosition { get; set; }

        /// <summary>Camera projection matrix used to calculate screen-relative sizes.</summary>
        public Matrix4x4 CameraProjection { get; set; } = Matrix4x4.Identity;

        /// <summary>The size of the viewport in pixels.</summary>
        public Vector2 ViewportSize { get; set; } = new Vector2(1000f, 1000f);

        /// <summary>The local-space bounding box of the selected object.</summary>
        public BoundingBox? ObjectLocalBounds { get; set; }

        /// <summary>Calculates the world-space size of the gizmo based on pixel constraints and object size.</summary>
        public float GetScreenSize() {
            float distance = Vector3.Distance(CameraPosition, Position);
            
            // Calculate how tall/wide the screen is in world units at this distance
            float m22 = MathF.Abs(CameraProjection.M22);
            float m11 = MathF.Abs(CameraProjection.M11);
            if (m22 < 1e-6f) m22 = 1.0f;
            if (m11 < 1e-6f) m11 = 1.0f;
            
            bool isPerspective = CameraProjection.M44 == 0;
            float worldHeight = isPerspective ? (2.0f * distance / m22) : (2.0f / m22);
            float worldWidth = isPerspective ? (2.0f * distance / m11) : (2.0f / m11);

            // Use the smaller dimension for our "base" world scale to pixels
            float minViewportDim = Math.Min(ViewportSize.X, ViewportSize.Y);
            float worldDim = ViewportSize.X < ViewportSize.Y ? worldWidth : worldHeight;
            float unitsPerPixel = worldDim / MathF.Max(1f, minViewportDim);

            // Calculate our pixel-based bounds in world-space units
            float minWorldSize = GizmoConfig.MinPixelSize * unitsPerPixel;
            float maxWorldSize = GizmoConfig.MaxPixelSize * unitsPerPixel;

            // Apply the percentage-based safety cap
            float maxRatioSize = worldDim * GizmoConfig.MaxViewportRatio;
            maxWorldSize = Math.Min(maxWorldSize, maxRatioSize);

            // Start with either the object-relative size or the default pixel size
            float targetSize;
            if (ObjectLocalBounds.HasValue) {
                var size = ObjectLocalBounds.Value.Max - ObjectLocalBounds.Value.Min;
                float objSize = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
                targetSize = objSize * GizmoConfig.ObjectSizeRatio;
            }
            else {
                targetSize = GizmoConfig.DefaultPixelSize * unitsPerPixel;
            }

            float finalMin = Math.Min(minWorldSize, maxWorldSize);
            return Math.Clamp(targetSize, finalMin, maxWorldSize);
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
        public ushort LandblockId { get; set; }

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
