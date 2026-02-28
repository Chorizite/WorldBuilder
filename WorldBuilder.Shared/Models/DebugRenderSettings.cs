using System.Numerics;
using WorldBuilder.Shared.Lib;
using System.ComponentModel;

namespace WorldBuilder.Shared.Models {
    public class DebugRenderSettings {
        public bool ShowBoundingBoxes { get; set; } = true;
        public bool SelectVertices { get; set; } = true;
        public bool SelectBuildings { get; set; } = true;
        public bool SelectStaticObjects { get; set; } = true;
        public bool SelectScenery { get; set; } = false;
        public bool SelectEnvCells { get; set; } = true;
        public bool SelectEnvCellStaticObjects { get; set; } = true;
        public bool SelectPortals { get; set; } = true;
        public bool EnableAnisotropicFiltering { get; set; } = true;

        public Vector4 VertexColor { get; set; } = LandscapeColorsSettings.Instance.Vertex;
        public Vector4 BuildingColor { get; set; } = LandscapeColorsSettings.Instance.Building;
        public Vector4 StaticObjectColor { get; set; } = LandscapeColorsSettings.Instance.StaticObject;
        public Vector4 SceneryColor { get; set; } = LandscapeColorsSettings.Instance.Scenery;
        public Vector4 EnvCellColor { get; set; } = LandscapeColorsSettings.Instance.EnvCell;
        public Vector4 EnvCellStaticObjectColor { get; set; } = LandscapeColorsSettings.Instance.EnvCellStaticObject;
        public Vector4 PortalColor { get; set; } = LandscapeColorsSettings.Instance.Portal;
    }
}
