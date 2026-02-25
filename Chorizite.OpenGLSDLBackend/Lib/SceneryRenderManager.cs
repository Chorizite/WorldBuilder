using Chorizite.Core.Lib;
using Chorizite.Core.Render;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Numerics;
using WorldBuilder.Shared.Services;
using BoundingBox = Chorizite.Core.Lib.BoundingBox;

namespace Chorizite.OpenGLSDLBackend.Lib {
    /// <summary>
    /// Manages scenery rendering: background generation, time-sliced GPU uploads, instanced drawing.
    /// Extends <see cref="ObjectRenderManagerBase"/> with terrain-based scenery generation.
    /// </summary>
    public class SceneryRenderManager : ObjectRenderManagerBase {
        private readonly IDocumentManager _documentManager;
        private readonly IDatReaderWriter _dats;
        private readonly StaticObjectRenderManager _staticObjectManager;

        // Caches
        private readonly ConcurrentDictionary<uint, Scene> _sceneCache = new();

        /// <summary>Scenery renders highlights even when the visible list is empty.</summary>
        protected override bool RenderHighlightsWhenEmpty => true;

        public SceneryRenderManager(GL gl, ILogger log, LandscapeDocument landscapeDoc,
            IDatReaderWriter dats, OpenGLGraphicsDevice graphicsDevice, ObjectMeshManager meshManager,
            StaticObjectRenderManager staticObjectManager, IDocumentManager documentManager, Frustum frustum)
            : base(gl, graphicsDevice, meshManager, log, landscapeDoc, frustum) {
            _dats = dats;
            _staticObjectManager = staticObjectManager;
            _documentManager = documentManager;
        }

        #region Public: Scenery-Specific API

        public void SubmitDebugShapes(DebugRenderer? debug, DebugRenderSettings settings) {
            if (debug == null || LandscapeDoc.Region == null || !settings.ShowBoundingBoxes || !settings.SelectScenery) return;

            foreach (var lb in _landblocks.Values) {
                if (!lb.GpuReady || !IsWithinRenderDistance(lb)) continue;
                if (GetLandblockFrustumResult(lb.GridX, lb.GridY) == FrustumTestResult.Outside) continue;

                foreach (var instance in lb.Instances) {
                    // Skip if instance is outside frustum
                    if (!_frustum.Intersects(instance.BoundingBox)) continue;

                    var isSelected = SelectedInstance.HasValue && SelectedInstance.Value.LandblockKey == GeometryUtils.PackKey(lb.GridX, lb.GridY) && SelectedInstance.Value.InstanceId == instance.InstanceId;
                    var isHovered = HoveredInstance.HasValue && HoveredInstance.Value.LandblockKey == GeometryUtils.PackKey(lb.GridX, lb.GridY) && HoveredInstance.Value.InstanceId == instance.InstanceId;

                    Vector4 color;
                    if (isSelected) color = LandscapeColorsSettings.Instance.Selection;
                    else if (isHovered) color = LandscapeColorsSettings.Instance.Hover;
                    else color = settings.SceneryColor;

                    debug.DrawBox(instance.LocalBoundingBox, instance.Transform, color);
                }
            }
        }

        public bool Raycast(Vector3 origin, Vector3 direction, out SceneRaycastHit hit) {
            hit = SceneRaycastHit.NoHit;

            foreach (var kvp in _landblocks) {
                if (!kvp.Value.GpuReady) continue;

                lock (kvp.Value) {
                    foreach (var inst in kvp.Value.Instances) {
                        var renderData = MeshManager.TryGetRenderData(inst.ObjectId);
                        if (renderData == null) continue;

                        // Broad phase: Bounding Box
                        if (!GeometryUtils.RayIntersectsBox(origin, direction, inst.BoundingBox.Min, inst.BoundingBox.Max, out _)) {
                            continue;
                        }

                        // Narrow phase: Mesh-precise raycast
                        if (MeshManager.IntersectMesh(renderData, inst.Transform, origin, direction, out float d)) {
                            if (d < hit.Distance) {
                                hit.Hit = true;
                                hit.Distance = d;
                                hit.Type = InspectorSelectionType.Scenery;
                                hit.ObjectId = inst.ObjectId;
                                hit.InstanceId = inst.InstanceId;
                                hit.Position = inst.WorldPosition;
                                hit.Rotation = inst.Rotation;
                                hit.LandblockId = (uint)((kvp.Key << 16) | 0xFFFE);
                            }
                        }
                    }
                }
            }

            return hit.Hit;
        }

        #endregion

        #region Protected: Generation Override

        protected override async Task GenerateForLandblockAsync(ObjectLandblock lb, CancellationToken ct) {
            try {
                var key = GeometryUtils.PackKey(lb.GridX, lb.GridY);

                // Early-out if no longer within render distance or no longer tracked
                if (!IsWithinRenderDistance(lb) || !_landblocks.ContainsKey(key)) return;
                ct.ThrowIfCancellationRequested();

                if (LandscapeDoc.Region is not ITerrainInfo regionInfo) return;

                var lbGlobalX = (uint)lb.GridX;
                var lbGlobalY = (uint)lb.GridY;

                // Ensure the landscape chunk is loaded and merged before we try to generate scenery from it
                var chunkX = (uint)(lbGlobalX / LandscapeChunk.LandblocksPerChunk);
                var chunkY = (uint)(lbGlobalY / LandscapeChunk.LandblocksPerChunk);
                var chunkId = LandscapeChunk.GetId(chunkX, chunkY);
                await LandscapeDoc.GetOrLoadChunkAsync(chunkId, _dats, _documentManager, ct);

                // Wait for static objects to be ready for this landblock
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct)) {
                    cts.CancelAfter(15000);
                    try {
                        await _staticObjectManager.WaitForInstancesAsync(key, cts.Token);
                    }
                    catch (OperationCanceledException) {
                        if (ct.IsCancellationRequested) throw;
                        Log.LogWarning("Timed out waiting for static objects for landblock ({X},{Y})", lb.GridX, lb.GridY);
                    }
                }

                var buildings = _staticObjectManager.GetLandblockInstances(key) ?? new List<SceneryInstance>();
                var pendingBuildings = _staticObjectManager.GetPendingLandblockInstances(key);
                if (pendingBuildings != null) {
                    buildings = pendingBuildings;
                }

                // Spatial index for buildings to speed up collisions (8x8 grid)
                var lbSizeUnits = regionInfo.LandblockSizeInUnits; // 192
                var buildingsGrid = new List<SceneryInstance>[8, 8];
                foreach (var b in buildings) {
                    var minX = (int)Math.Max(0, (b.BoundingBox.Min.X - regionInfo.MapOffset.X - lbGlobalX * lbSizeUnits) / 24f);
                    var maxX = (int)Math.Min(7, (b.BoundingBox.Max.X - regionInfo.MapOffset.X - lbGlobalX * lbSizeUnits) / 24f);
                    var minY = (int)Math.Max(0, (b.BoundingBox.Min.Y - regionInfo.MapOffset.Y - lbGlobalY * lbSizeUnits) / 24f);
                    var maxY = (int)Math.Min(7, (b.BoundingBox.Max.Y - regionInfo.MapOffset.Y - lbGlobalY * lbSizeUnits) / 24f);

                    for (int gx = minX; gx <= maxX; gx++) {
                        for (int gy = minY; gy <= maxY; gy++) {
                            buildingsGrid[gx, gy] ??= new List<SceneryInstance>();
                            buildingsGrid[gx, gy].Add(b);
                        }
                    }
                }

                var region = regionInfo.Region;
                var cellLength = regionInfo.LandblockCellLength; // 8
                var vertLength = regionInfo.LandblockVerticeLength; // 9

                // Extract per-landblock terrain entries (9x9 grid)
                var lbTerrainEntries = new TerrainEntry[vertLength * vertLength];
                for (int vx = 0; vx < vertLength; vx++) {
                    for (int vy = 0; vy < vertLength; vy++) {
                        var globalVx = (int)(lbGlobalX * cellLength + vx);
                        var globalVy = (int)(lbGlobalY * cellLength + vy);
                        if (globalVx < regionInfo.MapWidthInVertices && globalVy < regionInfo.MapHeightInVertices) {
                            var idx = globalVy * regionInfo.MapWidthInVertices + globalVx;
                            lbTerrainEntries[vx * vertLength + vy] = LandscapeDoc.GetCachedEntry((uint)idx);
                        }
                    }
                }

                var scenery = new List<SceneryInstance>();
                var blockCellX = (int)lbGlobalX * cellLength;
                var blockCellY = (int)lbGlobalY * cellLength;

                for (int i = 0; i < lbTerrainEntries.Length; i++) {
                    var entry = lbTerrainEntries[i];
                    var terrainType = entry.Type ?? 0;
                    var sceneType = entry.Scenery ?? 0;

                    if (terrainType >= region.TerrainInfo.TerrainTypes.Count) continue;
                    var terrainInfo = region.TerrainInfo.TerrainTypes[terrainType];
                    if (sceneType >= terrainInfo.SceneTypes.Count) continue;

                    var sceneInfoIdx = terrainInfo.SceneTypes[sceneType];
                    var sceneInfo = region.SceneInfo.SceneTypes[(int)sceneInfoIdx];
                    if (sceneInfo.Scenes.Count == 0) continue;

                    var cellX = i / vertLength;
                    var cellY = i % vertLength;
                    var globalCellX = (uint)(blockCellX + cellX);
                    var globalCellY = (uint)(blockCellY + cellY);

                    // Scene selection (deterministic pseudo-random)
                    var cellMat = globalCellY * (712977289u * globalCellX + 1813693831u) - 1109124029u * globalCellX + 2139937281u;
                    var offset = cellMat * 2.3283064e-10f;
                    var sceneIdx = (int)(sceneInfo.Scenes.Count * offset);
                    sceneIdx = Math.Clamp(sceneIdx, 0, sceneInfo.Scenes.Count - 1);
                    var sceneId = sceneInfo.Scenes[sceneIdx];

                    if (!_sceneCache.TryGetValue(sceneId, out var scene)) {
                        if (!_dats.Portal.TryGet<Scene>(sceneId, out scene)) continue;
                        _sceneCache[sceneId] = scene;
                    }
                    if (scene.Objects.Count == 0) continue;

                    var cellXMat = -1109124029 * (int)globalCellX;
                    var cellYMat = 1813693831 * (int)globalCellY;
                    var cellMat2 = unchecked(1360117743u * globalCellX * globalCellY + 1888038839u);

                    for (uint j = 0; j < scene.Objects.Count; j++) {
                        var obj = scene.Objects[(int)j];
                        if (obj.ObjectId == 0) continue;

                        var noise = (uint)(cellXMat + cellYMat - cellMat2 * (23399 + j)) * 2.3283064e-10f;
                        if (noise >= obj.Frequency) continue;

                        var localPos = SceneryHelpers.Displace(obj, globalCellX, globalCellY, j);
                        var cellSize = regionInfo.CellSizeInUnits; // 24
                        var lx = cellX * cellSize + localPos.X;
                        var ly = cellY * cellSize + localPos.Y;

                        if (lx < 0 || ly < 0 || lx >= lbSizeUnits || ly >= lbSizeUnits) continue;

                        // Road check
                        if (TerrainGeometryGenerator.OnRoad(new Vector3(lx, ly, 0), lbTerrainEntries)) continue;

                        // Height and normal
                        var lbOffset = new Vector3(lx, ly, 0);
                        var z = TerrainGeometryGenerator.GetHeight(region, lbTerrainEntries, lbGlobalX, lbGlobalY, lbOffset);
                        lbOffset.Z = z;

                        var normal = TerrainGeometryGenerator.GetNormal(region, lbTerrainEntries, lbGlobalX, lbGlobalY, lbOffset);
                        if (!SceneryHelpers.CheckSlope(obj, normal.Z)) continue;

                        Quaternion quat;
                        if (obj.Align != 0) {
                            quat = SceneryHelpers.ObjAlign(obj, normal, z, localPos);
                        }
                        else {
                            quat = SceneryHelpers.RotateObj(obj, globalCellX, globalCellY, j, localPos);
                        }

                        var scaleVal = SceneryHelpers.ScaleObj(obj, globalCellX, globalCellY, j);
                        var scale = new Vector3(scaleVal);

                        var worldOrigin = new Vector3(new Vector2(lbGlobalX * lbSizeUnits + lx, lbGlobalY * lbSizeUnits + ly) + regionInfo.MapOffset, z);

                        var transform = Matrix4x4.CreateScale(scale)
                            * Matrix4x4.CreateFromQuaternion(quat)
                            * Matrix4x4.CreateTranslation(worldOrigin);

                        var isSetup = (obj.ObjectId >> 24) == 0x02;

                        var bounds = MeshManager.GetBounds(obj.ObjectId, isSetup);
                        var localBbox = bounds.HasValue ? new BoundingBox(bounds.Value.Min, bounds.Value.Max) : default;
                        var bbox = localBbox.Transform(transform);

                        var instance = new SceneryInstance {
                            ObjectId = obj.ObjectId,
                            InstanceId = (uint)scenery.Count,
                            IsSetup = isSetup,
                            WorldPosition = worldOrigin,
                            Rotation = quat,
                            Scale = scale,
                            Transform = transform,
                            LocalBoundingBox = localBbox,
                            BoundingBox = bbox
                        };

                        // Collision detection using spatial index
                        var gx2 = (int)Math.Clamp(lx / 24f, 0, 7);
                        var gy2 = (int)Math.Clamp(ly / 24f, 0, 7);
                        var nearbyBuildings = buildingsGrid[gx2, gy2];

                        if (nearbyBuildings != null && Collision(nearbyBuildings, instance))
                            continue;

                        scenery.Add(instance);
                    }
                }

                lb.PendingInstances = scenery;

                if (scenery.Count > 0) {
                    Log.LogTrace("Generated {Count} scenery instances for landblock ({X},{Y})", scenery.Count, lb.GridX, lb.GridY);
                }

                // Prepare mesh data for unique objects on background thread
                await PrepareMeshesForInstances(scenery, ct);

                lb.MeshDataReady = true;
                _uploadQueue.Enqueue(lb);
            }
            catch (OperationCanceledException) {
                // Ignore cancellations
            }
            catch (Exception ex) {
                Log.LogError(ex, "Error generating scenery for landblock ({X},{Y})", lb.GridX, lb.GridY);
            }
        }

        #endregion

        #region Private: Collision Detection

        private bool Collision(List<SceneryInstance> instances, SceneryInstance target) {
            foreach (var instance in instances) {
                if (target.BoundingBox.Intersects2D(instance.BoundingBox)) {
                    return true;
                }
            }
            return false;
        }

        #endregion
    }
}
