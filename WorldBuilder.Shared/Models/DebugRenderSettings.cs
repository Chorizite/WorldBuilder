using System.Numerics;

namespace WorldBuilder.Shared.Models {
    public class DebugRenderSettings {
        public bool ShowBoundingBoxes { get; set; } = true;
        public bool SelectVertices { get; set; } = true;
        public bool SelectBuildings { get; set; } = true;
        public bool SelectStaticObjects { get; set; } = true;
        public bool SelectScenery { get; set; } = false;

        public Vector4 VertexColor { get; set; } = new Vector4(1.0f, 1.0f, 0.0f, 1.0f); // Yellow
        public Vector4 BuildingColor { get; set; } = new Vector4(1.0f, 0.0f, 1.0f, 1.0f); // Blue
        public Vector4 StaticObjectColor { get; set; } = new Vector4(0.3f, 0.5f, 0.9f, 1.0f); // Red
        public Vector4 SceneryColor { get; set; } = new Vector4(0.0f, 0.8f, 0.0f, 1.0f); // Green
    }
}
