using Chorizite.Core.Render;
using System.Numerics;
using WorldBuilder.Lib;
using WorldBuilder.Tools.Landscape;

namespace WorldBuilder.Tools.Landscape {
    public struct MouseState {
        public Vector2 Position;
        public bool LeftPressed;
        public bool RightPressed;
        public bool MiddlePressed;
        public Vector2 Delta;
        public bool IsOverTerrain;
        public TerrainRaycast.TerrainRaycastHit? TerrainHit;
    }

    public interface ITerrainTool {
        string Name { get; }

        bool HandleMouseDown(MouseState mouseState, TerrainEditingContext context) => false;
        bool HandleMouseUp(MouseState mouseState, TerrainEditingContext context) => false;
        bool HandleMouseMove(MouseState mouseState, TerrainEditingContext context) => false;

        void Update(double deltaTime, TerrainEditingContext context) { }

        void OnActivated(TerrainEditingContext context) { }
        void OnDeactivated(TerrainEditingContext context) { }

        void RenderOverlay(TerrainEditingContext context, IRenderer renderer, ICamera camera, float aspectRatio) { }
    }
}