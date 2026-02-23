using System.Numerics;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Shared.Models {
    public class DebugRenderSettings {
        public bool ShowBoundingBoxes { get; set; } = true;
        public bool SelectVertices { get; set; } = true;
        public bool SelectBuildings { get; set; } = true;
        public bool SelectStaticObjects { get; set; } = true;
        public bool SelectScenery { get; set; } = false;

        public Vector4 VertexColor { get; set; } = RenderColors.Vertex; // Yellow
        public Vector4 BuildingColor { get; set; } = RenderColors.Building; // Magenta
        public Vector4 StaticObjectColor { get; set; } = RenderColors.StaticObject; // Light Blue
        public Vector4 SceneryColor { get; set; } = RenderColors.Scenery; // Green
    }
}
