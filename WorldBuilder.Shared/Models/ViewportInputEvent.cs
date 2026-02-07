using System.Numerics;

namespace WorldBuilder.Shared.Models
{
    public class ViewportInputEvent
    {
        public Vector2 Position { get; set; }
        public Vector2 Delta { get; set; }
        public Vector2 ViewportSize { get; set; }
        public bool IsLeftDown { get; set; }
        public bool IsRightDown { get; set; }
        public bool ShiftDown { get; set; }
        public bool CtrlDown { get; set; }
        public bool AltDown { get; set; }
        public int? ReleasedButton { get; set; }
    }
}
