using System.Numerics;

namespace WorldBuilder.Shared.Models {
    /// <summary>
    /// Represents an input event occurring within a 3D viewport.
    /// </summary>
    public class ViewportInputEvent {
        /// <summary>The current mouse position in viewport coordinates.</summary>
        public Vector2 Position { get; set; }
        /// <summary>The change in mouse position since the last event.</summary>
        public Vector2 Delta { get; set; }
        /// <summary>The size of the viewport.</summary>
        public Vector2 ViewportSize { get; set; }
        /// <summary>Whether the left mouse button is currently down.</summary>
        public bool IsLeftDown { get; set; }
        /// <summary>Whether the right mouse button is currently down.</summary>
        public bool IsRightDown { get; set; }
        /// <summary>Whether the Shift key is currently down.</summary>
        public bool ShiftDown { get; set; }
        /// <summary>Whether the Ctrl key is currently down.</summary>
        public bool CtrlDown { get; set; }
        /// <summary>Whether the Alt key is currently down.</summary>
        public bool AltDown { get; set; }
        /// <summary>The button that was released, if any.</summary>
        public int? ReleasedButton { get; set; }
        /// <summary>The key that was pressed or released, if any.</summary>
        public string? Key { get; set; }
        /// <summary>The mouse wheel delta, if any.</summary>
        public float? WheelDelta { get; set; }
        /// <summary>The ray origin in world space.</summary>
        public Vector3 RayOrigin { get; set; }
        /// <summary>The ray direction in world space.</summary>
        public Vector3 RayDirection { get; set; }
    }
}