namespace Chorizite.OpenGLSDLBackend.Lib;

public class GpuResourceManager {
    private float _lastTerrainUploadTime;
    private float _lastSceneryUploadTime;
    private float _lastStaticObjectUploadTime;
    private float _lastEnvCellUploadTime;

    public float LastTerrainUploadTime => _lastTerrainUploadTime;
    public float LastSceneryUploadTime => _lastSceneryUploadTime;
    public float LastStaticObjectUploadTime => _lastStaticObjectUploadTime;
    public float LastEnvCellUploadTime => _lastEnvCellUploadTime;

    public void ProcessUploads(float totalBudget, IRenderManager? terrainManager, 
        IRenderManager? staticObjectManager, IRenderManager? envCellManager, 
        IRenderManager? sceneryManager, IRenderManager? portalManager) {
        
        // Fair budget distribution for GPU uploads
        // We prioritize Terrain and Buildings/EnvCells over Scenery
        float terrainBudget = totalBudget * 0.4f;
        float structuralBudget = totalBudget * 0.4f;
        float sceneryBudget = totalBudget * 0.2f;

        _lastTerrainUploadTime = terrainManager?.ProcessUploads(terrainBudget) ?? 0;
        float remainingTerrain = terrainBudget - _lastTerrainUploadTime;

        // Static objects and EnvCells share structural budget
        float structuralStart = structuralBudget + remainingTerrain;
        _lastStaticObjectUploadTime = staticObjectManager?.ProcessUploads(structuralStart) ?? 0;
        float remainingStructural = structuralStart - _lastStaticObjectUploadTime;

        _lastEnvCellUploadTime = envCellManager?.ProcessUploads(remainingStructural) ?? 0;
        remainingStructural -= _lastEnvCellUploadTime;

        // Scenery gets leftovers
        _lastSceneryUploadTime = sceneryManager?.ProcessUploads(sceneryBudget + remainingStructural) ?? 0;
        float remainingScenery = (sceneryBudget + remainingStructural) - _lastSceneryUploadTime;

        portalManager?.ProcessUploads(remainingScenery);
    }
}