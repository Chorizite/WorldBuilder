using Chorizite.Core.Render;
using System.Numerics;
using WorldBuilder.Editors.Landscape;

namespace WorldBuilder.Lib {
    public struct MouseState {
        public Vector2 Position;
        public bool LeftPressed;
        public bool RightPressed;
        public bool MiddlePressed;
        public Vector2 Delta;
        public bool IsOverTerrain;
        public TerrainRaycast.TerrainRaycastHit? TerrainHit;
    }
}