using System.Numerics;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools;

public interface ILandscapeRaycastService {
    bool RaycastStaticObject(Vector3 origin, Vector3 direction, bool includeBuildings, bool includeStaticObjects, out SceneRaycastHit hit, ObjectId ignoreInstanceId = default);
    bool RaycastScenery(Vector3 origin, Vector3 direction, out SceneRaycastHit hit);
    bool RaycastPortals(Vector3 origin, Vector3 direction, out SceneRaycastHit hit);
    bool RaycastEnvCells(Vector3 origin, Vector3 direction, bool includeCells, bool includeStaticObjects, out SceneRaycastHit hit, ObjectId ignoreInstanceId = default);
    TerrainRaycastHit RaycastTerrain(float x, float y, Vector2 viewportSize, ICamera camera);
    uint GetEnvCellAt(Vector3 worldPos);
}
