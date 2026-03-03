using Chorizite.Core.Lib;
using Chorizite.Core.Render;
using Silk.NET.OpenGL;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace Chorizite.OpenGLSDLBackend.Lib;

public class VisibilityManager {
    private readonly GL _gl;
    private readonly Frustum _cullingFrustum = new();
    private readonly List<(ushort LbKey, BuildingPortalGPU Building)> _visibleBuildingPortals = new();
    private readonly List<(ushort LbKey, BuildingPortalGPU Building)> _buildingsWithCurrentCell = new();
    private readonly List<(ushort LbKey, BuildingPortalGPU Building)> _otherBuildings = new();
    private readonly HashSet<uint> _currentEnvCellIds = new();

    public Frustum CullingFrustum => _cullingFrustum;

    public VisibilityManager(GL gl) {
        _gl = gl;
    }

    public void UpdateFrustum(Matrix4x4 viewProjection) {
        _cullingFrustum.Update(viewProjection);
    }

    public FrustumTestResult GetLandblockFrustumResult(LandscapeDocument? landscapeDoc, int gridX, int gridY) {
        if (landscapeDoc?.Region == null) return FrustumTestResult.Outside;
        var region = landscapeDoc.Region;
        var lbSize = region.CellSizeInUnits * region.LandblockCellLength;
        var offset = region.MapOffset;
        var minX = gridX * lbSize + offset.X;
        var minY = gridY * lbSize + offset.Y;
        var maxX = (gridX + 1) * lbSize + offset.X;
        var maxY = (gridY + 1) * lbSize + offset.Y;

        var box = new Chorizite.Core.Lib.BoundingBox(
            new Vector3(minX, minY, -1000f),
            new Vector3(maxX, maxY, 5000f)
        );
        return _cullingFrustum.TestBox(box);
    }

    public void PrepareVisibility(EditorState state, uint currentEnvCellId, PortalRenderManager? portalManager, EnvCellRenderManager? envCellManager, Matrix4x4 snapshotVP, bool isInside, out HashSet<uint>? visibleEnvCells) {
        visibleEnvCells = null;
        if (state.ShowEnvCells && envCellManager != null) {
            visibleEnvCells = new HashSet<uint>();
            if (isInside) {
                _buildingsWithCurrentCell.Clear();
                portalManager?.GetBuildingPortalsByCellId(currentEnvCellId, _buildingsWithCurrentCell);
                foreach (var (_, building) in _buildingsWithCurrentCell) {
                    foreach (var id in building.EnvCellIds) visibleEnvCells.Add(id);
                }
            }
            _visibleBuildingPortals.Clear();
            portalManager?.GetVisibleBuildingPortals(_visibleBuildingPortals);
            for (int i = _visibleBuildingPortals.Count - 1; i >= 0; i--) {
                if (_visibleBuildingPortals[i].Building.VertexCount <= 0) {
                    _visibleBuildingPortals.RemoveAt(i);
                }
            }
            foreach (var (_, building) in _visibleBuildingPortals) {
                // Prepare all EnvCells for buildings in the frustum.
                // Portal-based rendering will handle the actual occlusion.
                foreach (var id in building.EnvCellIds) visibleEnvCells.Add(id);
            }
        }
    }

    public void RenderInsideOut(uint currentEnvCellId, RenderPass pass1RenderPass, Matrix4x4 snapshotVP, Matrix4x4 snapshotView, Matrix4x4 snapshotProj, Vector3 snapshotPos, float snapshotFov, 
        EditorState state, PortalRenderManager? portalManager, EnvCellRenderManager? envCellManager, TerrainRenderManager? terrainManager, 
        SceneryRenderManager? sceneryManager, StaticObjectRenderManager? staticObjectManager, IShader? sceneryShader) {
        
        bool didInsideStencil = false;
        if (_buildingsWithCurrentCell.Count > 0) {
            didInsideStencil = true;
            _gl.Enable(EnableCap.StencilTest);
            _gl.ClearStencil(0);
            _gl.Clear(ClearBufferMask.StencilBufferBit);

            // Step 1: Write stencil Bit 1 (0x01) for all portals of the building(s) we are in.
            // This marks the "doorways" out of our current building.
            _gl.Disable(EnableCap.CullFace);
            _gl.StencilFunc(StencilFunction.Always, 1, 0xFF);
            _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
            _gl.StencilMask(0x01); // Only write Bit 1
            _gl.ColorMask(false, false, false, false);
            _gl.DepthMask(false);
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Always);

            foreach (var (lbKey, building) in _buildingsWithCurrentCell) {
                portalManager?.RenderBuildingStencilMask(building, snapshotVP, false);
            }

            // Step 2: Punch through depth buffer at doorways so outside can be seen.
            _gl.DepthMask(true);
            _gl.DepthFunc(DepthFunction.Always);
            foreach (var (lbKey, building) in _buildingsWithCurrentCell) {
                portalManager?.RenderBuildingStencilMask(building, snapshotVP, true);
            }
        }

        // Step 3: Render EnvCells of the current building(s).
        // These should ALWAYS render, not restricted by their own portals (since we are inside).
        _gl.ColorMask(true, true, true, false);
        _gl.DepthMask(true);
        _gl.Disable(EnableCap.StencilTest);
        _gl.DepthFunc(DepthFunction.Less);
        sceneryShader?.Bind();

        if (_buildingsWithCurrentCell.Count > 0) {
            _currentEnvCellIds.Clear();
            foreach (var (lbKey, building) in _buildingsWithCurrentCell) {
                foreach (var id in building.EnvCellIds) _currentEnvCellIds.Add(id);
            }
            envCellManager!.Render(pass1RenderPass, _currentEnvCellIds);

            if (state.EnableTransparencyPass) {
                _gl.DepthMask(false);
                envCellManager!.Render(RenderPass.Transparent, _currentEnvCellIds);
                _gl.DepthMask(true);
            }
        }

        // Step 4: Restrict exterior (Terrain/Scenery/StaticObjects) through portals.
        if (didInsideStencil) {
            _gl.Enable(EnableCap.StencilTest);
            _gl.StencilFunc(StencilFunction.Equal, 1, 0x01);
            _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            _gl.StencilMask(0x00);
            _gl.ColorMask(true, true, true, false);
            _gl.DepthMask(true);
            _gl.Enable(EnableCap.CullFace);
            _gl.DepthFunc(DepthFunction.Less);
        }

        // Render terrain after EnvCells when inside, so that terrain only renders through portal openings 
        // (where there are no interior walls to occlude it).
        if (terrainManager != null) {
            terrainManager.Render(snapshotView, snapshotProj, snapshotVP, snapshotPos, snapshotFov);
            sceneryShader?.Bind();
        }

        if (state.ShowScenery) {
            sceneryManager?.Render(pass1RenderPass);
        }

        if (state.ShowStaticObjects || state.ShowBuildings) {
            staticObjectManager?.Render(pass1RenderPass);
        }

        // Step 5: Render EnvCells of OTHER buildings, masked by our portals AND their own portals.
        if (didInsideStencil) {
            _otherBuildings.Clear();
            foreach (var p in _visibleBuildingPortals) {
                if (!p.Building.EnvCellIds.Contains(currentEnvCellId)) {
                    _otherBuildings.Add(p);
                }
            }

            if (_otherBuildings.Count > 0) {
                _gl.Enable(EnableCap.StencilTest);
                _gl.ColorMask(false, false, false, false);
                _gl.DepthMask(false);
                _gl.DepthFunc(DepthFunction.Lequal);

                foreach (var (lbKey, building) in _otherBuildings) {
                    // Read back the previous frame's occlusion query result.
                    if (building.QueryId != 0) {
                        if (building.QueryStarted) {
                            _gl.GetQueryObject(building.QueryId, QueryObjectParameterName.ResultAvailable, out int available);
                            if (available != 0) {
                                _gl.GetQueryObject(building.QueryId, QueryObjectParameterName.Result, out int samplesPassed);
                                building.WasVisible = samplesPassed > 0;
                            }
                        }

                        _gl.BeginQuery(QueryTarget.SamplesPassed, building.QueryId);
                        building.QueryStarted = true;
                    }

                    // a. Mark Bit 2 (0x02) for this building's portals, BUT ONLY where Bit 1 (0x01) is set.
                    // We use Ref=3, Mask=0x02 to set Bit 2 while Bit 1 remains.
                    _gl.StencilFunc(StencilFunction.Equal, 3, 0x01); // Match Bit 1
                    _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
                    _gl.StencilMask(0x02); // Only write to Bit 2
                    _gl.Disable(EnableCap.CullFace);
                    portalManager?.RenderBuildingStencilMask(building, snapshotVP, false);

                    if (building.QueryId != 0) {
                        _gl.EndQuery(QueryTarget.SamplesPassed);
                    }

                    // b. Clear depth where Stencil == 3 (Inside our portal AND its portal).
                    // This is necessary because building A's interior cells may have 
                    // written depth into the doorway.
                    _gl.StencilFunc(StencilFunction.Equal, 3, 0x03);
                    _gl.StencilMask(0x00);
                    _gl.DepthMask(true);
                    _gl.DepthFunc(DepthFunction.Always);
                    portalManager?.RenderBuildingStencilMask(building, snapshotVP, true);

                    // c. Render this building's EnvCells where Stencil == 3 (GPU will depth/stencil cull).
                    // We render regardless of WasVisible here because we are inside and want to avoid
                    // latency or logic bugs with portal-to-portal occlusion. Stencil/depth will cull.
                    _gl.ColorMask(true, true, true, false);
                    _gl.DepthFunc(DepthFunction.Less);
                    _gl.Enable(EnableCap.CullFace);
                    sceneryShader?.Bind();
                    envCellManager!.Render(pass1RenderPass, building.EnvCellIds);

                    if (state.EnableTransparencyPass) {
                        _gl.DepthMask(false);
                        envCellManager!.Render(RenderPass.Transparent, building.EnvCellIds);
                        _gl.DepthMask(true);
                    }

                    // d. Reset Stencil back to 1 (clear Bit 2) for the next building.
                    _gl.ColorMask(false, false, false, false);
                    _gl.DepthMask(false);
                    _gl.StencilMask(0x02);
                    _gl.StencilFunc(StencilFunction.Always, 1, 0x02); // Replace Bit 2 with 0 (Ref=1 has Bit 2=0)
                    _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
                    portalManager?.RenderBuildingStencilMask(building, snapshotVP, false);
                }
                _gl.DepthFunc(DepthFunction.Less);
            }
        }

        if (didInsideStencil) {
            _gl.Disable(EnableCap.StencilTest);
            _gl.StencilMask(0xFF);
            _gl.ColorMask(true, true, true, false);
        }
    }

    public void RenderOutsideIn(RenderPass pass1RenderPass, Matrix4x4 snapshotVP, Vector3 snapshotPos,
        EditorState state, PortalRenderManager? portalManager, EnvCellRenderManager? envCellManager, StaticObjectRenderManager? staticObjectManager, IShader? sceneryShader) {
        
        bool didStencil = false;

        if (_visibleBuildingPortals.Count > 0) {
            didStencil = true;
            _gl.Enable(EnableCap.StencilTest);
            _gl.ClearStencil(0);
            _gl.Clear(ClearBufferMask.StencilBufferBit);

            // Step 1: Write stencil for all portal polygons.
            // DepthFunc(Always) so portals always mark the stencil.
            // No color or depth writes — just stencil.
            // Disable backface culling: portal polygons face inward
            // (into the building) so they'd be culled when viewed from outside.
            _gl.Disable(EnableCap.CullFace);
            _gl.StencilFunc(StencilFunction.Always, 1, 0xFF);
            _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
            _gl.StencilMask(0xFF);
            _gl.ColorMask(false, false, false, false);
            _gl.DepthMask(false);
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Less);

            foreach (var (lbKey, building) in _visibleBuildingPortals) {
                // Read back the previous frame's occlusion query result to avoid CPU stall.
                // If the portal became visible this frame, it will pass the depth test,
                // the query will count it, and next frame its EnvCells will be rendered.
                if (building.QueryId != 0) {
                    if (building.QueryStarted) {
                        _gl.GetQueryObject(building.QueryId, QueryObjectParameterName.ResultAvailable, out int available);
                        if (available != 0) {
                            _gl.GetQueryObject(building.QueryId, QueryObjectParameterName.Result, out int samplesPassed);
                            building.WasVisible = samplesPassed > 0;
                        }
                    }

                    _gl.BeginQuery(QueryTarget.SamplesPassed, building.QueryId);
                    building.QueryStarted = true;
                }

                portalManager?.RenderBuildingStencilMask(building, snapshotVP, false);

                if (building.QueryId != 0) {
                    _gl.EndQuery(QueryTarget.SamplesPassed);
                }
            }
            _gl.DepthFunc(DepthFunction.Less);

            // Step 2: Clear depth to far plane ONLY where stencil==1.
            // This removes terrain depth at portal openings so EnvCells
            // can render over terrain. Shader writes gl_FragDepth = 1.0.
            _gl.StencilFunc(StencilFunction.Equal, 1, 0xFF);
            _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            _gl.StencilMask(0x00);
            _gl.DepthMask(true);
            _gl.DepthFunc(DepthFunction.Always);

            foreach (var (lbKey, building) in _visibleBuildingPortals) {
                portalManager?.RenderBuildingStencilMask(building, snapshotVP, true);
            }

            // Re-enable backface culling for depth repair
            _gl.Enable(EnableCap.CullFace);

            // Step 3: Depth repair — re-render building walls depth-only
            // where stencil==1. This restores wall depth that was cleared
            // in step 2, preventing see-through-walls.
            // StencilFunc still Equal,1 — only repair where portal was marked.
            _gl.DepthFunc(DepthFunction.Less);
            // ColorMask still false, DepthMask still true

            sceneryShader?.Bind();

            if (state.ShowStaticObjects || state.ShowBuildings) {
                staticObjectManager?.Render(pass1RenderPass);
            }

            // Step 4: Prepare state for EnvCell rendering through stencil.
            // At doorway: depth=far_plane, EnvCells pass ✓
            // At wall from side: wall depth restored, EnvCells fail ✓
            _gl.ColorMask(true, true, true, false);
            // StencilFunc still Equal,1; DepthFunc still Less
        }

        // Render EnvCells through portal masks with normal depth test.
        if (didStencil) {
            // Step 5: Render EnvCells through portal masks with normal depth test.
            _gl.StencilFunc(StencilFunction.Equal, 1, 0xFF);
            _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            _gl.ColorMask(true, true, true, false);
            _gl.DepthFunc(DepthFunction.Less);

            sceneryShader?.Bind();
            envCellManager!.Render(pass1RenderPass, null);

            if (state.EnableTransparencyPass) {
                _gl.DepthMask(false);
                envCellManager!.Render(RenderPass.Transparent, null);
                _gl.DepthMask(true);
            }
        }
        else {
            envCellManager!.Render(pass1RenderPass, null);

            if (state.EnableTransparencyPass) {
                _gl.DepthMask(false);
                envCellManager!.Render(RenderPass.Transparent, null);
                _gl.DepthMask(true);
            }
        }

        if (didStencil) {
            _gl.Disable(EnableCap.StencilTest);
            _gl.StencilMask(0xFF);
            _gl.ColorMask(true, true, true, false);
        }
    }

    public void RenderEnvCellsFallback(EnvCellRenderManager? envCellManager, RenderPass pass1RenderPass, EditorState state) {
        envCellManager?.Render(pass1RenderPass);
        if (state.EnableTransparencyPass) {
            _gl.DepthMask(false);
            envCellManager?.Render(RenderPass.Transparent);
            _gl.DepthMask(true);
        }
    }
}