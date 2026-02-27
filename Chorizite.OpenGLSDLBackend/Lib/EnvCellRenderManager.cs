using Chorizite.Core.Lib;
using Chorizite.Core.Render;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using System;
using DatReaderWriter.Types;
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
    /// Manages rendering of building interior cells (EnvCells) visible from the outside.
    /// Extends <see cref="ObjectRenderManagerBase"/> to fit in the same pipeline as StaticObjectRenderManager.
    /// </summary>
    public class EnvCellRenderManager : ObjectRenderManagerBase {
        private readonly IDatReaderWriter _dats;

        // Instance readiness coordination
        private readonly ConcurrentDictionary<ushort, TaskCompletionSource> _instanceReadyTcs = new();
        private readonly object _tcsLock = new();

        private bool _showEnvCells = true;

        // Grouped instances by cell ID for efficient filtered rendering
        private readonly Dictionary<uint, Dictionary<ulong, List<Matrix4x4>>> _cellBatches = new();

        protected override bool RenderHighlightsWhenEmpty => true;

        protected override int MaxConcurrentGenerations => 21;

        public EnvCellRenderManager(GL gl, ILogger log, LandscapeDocument landscapeDoc,
            IDatReaderWriter dats, OpenGLGraphicsDevice graphicsDevice, ObjectMeshManager meshManager, Frustum frustum)
            : base(gl, graphicsDevice, meshManager, log, landscapeDoc, frustum) {
            _dats = dats;
        }

        #region Public API

        /// <summary>
        /// Sets the visibility filter for EnvCells.
        /// Call before <see cref="ObjectRenderManagerBase.PrepareRenderBatches"/>.
        /// </summary>
        public void SetVisibilityFilters(bool showEnvCells) {
            _showEnvCells = showEnvCells;
        }

        public uint GetEnvCellAt(Vector3 pos, bool onlyEntryCells = false) {
            if (LandscapeDoc.Region == null) return 0;

            var lbSize = LandscapeDoc.Region.LandblockSizeInUnits;
            var mapPos = new Vector2(pos.X, pos.Y) - LandscapeDoc.Region.MapOffset;
            int lbX = (int)Math.Floor(mapPos.X / lbSize);
            int lbY = (int)Math.Floor(mapPos.Y / lbSize);

            if (lbX < 0 || lbY < 0 || lbX >= LandscapeDoc.Region.MapWidthInLandblocks || lbY >= LandscapeDoc.Region.MapHeightInLandblocks) return 0;

            var key = GeometryUtils.PackKey(lbX, lbY);

            if (_landblocks.TryGetValue(key, out var lb)) {
                if (!lb.InstancesReady || !lb.MeshDataReady) return 0xFFFFFFFF; // Data or meshes not ready
                lock (lb) {
                    // Check both active and pending instances to avoid race conditions
                    if (CheckInstances(lb.Instances, pos, onlyEntryCells, out var cellId)) return cellId;
                    if (lb.PendingInstances != null && CheckInstances(lb.PendingInstances, pos, onlyEntryCells, out cellId)) return cellId;
                }
                return 0; // Definitely not in an EnvCell in this loaded landblock
            }
            return 0xFFFFFFFF; // Landblock not loaded yet
        }

        private bool CheckInstances(List<SceneryInstance> instances, Vector3 pos, bool onlyEntryCells, out uint cellId) {
            cellId = 0;
            // Add a small vertical epsilon to account for the rendering Z-offset (0.02f)
            // and floating point precision.
            var testPos = pos + new Vector3(0, 0, 0.1f);
            foreach (var instance in instances) {
                var type = InstanceIdConstants.GetType(instance.InstanceId);
                if (type != InspectorSelectionType.EnvCell) continue;
                if (onlyEntryCells && !instance.IsEntryCell) continue;

                // Expand the bounding box slightly (0.1 units) for the containment test
                // to handle precision issues and teleporting to boundaries.
                var bbox = instance.BoundingBox;
                if (testPos.X >= bbox.Min.X - 0.1f && testPos.X <= bbox.Max.X + 0.1f &&
                    testPos.Y >= bbox.Min.Y - 0.1f && testPos.Y <= bbox.Max.Y + 0.1f &&
                    testPos.Z >= bbox.Min.Z - 0.1f && testPos.Z <= bbox.Max.Z + 0.1f) {
                    cellId = InstanceIdConstants.GetRawId(instance.InstanceId);
                    return true;
                }
            }
            return false;
        }

        public bool Raycast(Vector3 rayOrigin, Vector3 rayDirection, bool includeCells, bool includeStaticObjects, out SceneRaycastHit hit) {
            hit = SceneRaycastHit.NoHit;

            foreach (var (key, lb) in _landblocks) {
                if (!lb.InstancesReady) continue;

                lock (lb) {
                    foreach (var instance in lb.Instances) {
                        var type = InstanceIdConstants.GetType(instance.InstanceId);
                        if (type == InspectorSelectionType.EnvCell && !includeCells) continue;
                        if (type == InspectorSelectionType.EnvCellStaticObject && !includeStaticObjects) continue;

                        var renderData = MeshManager.TryGetRenderData(instance.ObjectId);
                        if (renderData == null) continue;

                        // Broad phase: Bounding Box
                        if (instance.BoundingBox.Max != instance.BoundingBox.Min) {
                            if (!GeometryUtils.RayIntersectsBox(rayOrigin, rayDirection, instance.BoundingBox.Min, instance.BoundingBox.Max, out _)) {
                                continue;
                            }
                        }

                        // Narrow phase: Mesh-precise raycast
                        if (MeshManager.IntersectMesh(renderData, instance.Transform, rayOrigin, rayDirection, out float d, out Vector3 normal)) {
                            if (d < hit.Distance) {
                                hit.Hit = true;
                                hit.Distance = d;
                                hit.Type = type;
                                hit.ObjectId = (uint)instance.ObjectId;
                                hit.InstanceId = instance.InstanceId;
                                hit.SecondaryId = InstanceIdConstants.GetSecondaryId(instance.InstanceId);
                                hit.Position = instance.WorldPosition;
                                hit.LocalPosition = instance.LocalPosition;
                                hit.Rotation = instance.Rotation;
                                hit.LandblockId = (uint)((key << 16) | 0xFFFE);
                                hit.Normal = normal;
                            }
                        }
                    }
                }
            }

            return hit.Hit;
        }

        public void SubmitDebugShapes(DebugRenderer? debug, DebugRenderSettings settings) {
            if (debug == null || LandscapeDoc.Region == null || !settings.ShowBoundingBoxes) return;

            foreach (var lb in _landblocks.Values) {
                if (!lb.InstancesReady || !IsWithinRenderDistance(lb)) continue;
                if (GetLandblockFrustumResult(lb.GridX, lb.GridY) == FrustumTestResult.Outside) continue;

                foreach (var instance in lb.Instances) {
                    var type = InstanceIdConstants.GetType(instance.InstanceId);
                    if (type == InspectorSelectionType.EnvCell && !settings.SelectEnvCells) continue;
                    if (type == InspectorSelectionType.EnvCellStaticObject && !settings.SelectEnvCellStaticObjects) continue;

                    // Skip if instance is outside frustum
                    if (!_frustum.Intersects(instance.BoundingBox)) continue;

                    var isSelected = SelectedInstance.HasValue && SelectedInstance.Value.LandblockKey == GeometryUtils.PackKey(lb.GridX, lb.GridY) && SelectedInstance.Value.InstanceId == instance.InstanceId;
                    var isHovered = HoveredInstance.HasValue && HoveredInstance.Value.LandblockKey == GeometryUtils.PackKey(lb.GridX, lb.GridY) && HoveredInstance.Value.InstanceId == instance.InstanceId;

                    Vector4 color;
                    if (isSelected) color = LandscapeColorsSettings.Instance.Selection;
                    else if (isHovered) color = LandscapeColorsSettings.Instance.Hover;
                    else if (type == InspectorSelectionType.EnvCell) color = settings.EnvCellColor;
                    else color = settings.EnvCellStaticObjectColor;

                    debug.DrawBox(instance.LocalBoundingBox, instance.Transform, color);
                }
            }
        }

        #endregion

        #region Protected: Overrides

        public override void PrepareRenderBatches(Matrix4x4 viewProjectionMatrix, Vector3 cameraPosition) {
            if (!_initialized || cameraPosition.Z > 4000) return;

            // Clear previous frame data
            _visibleGroups.Clear();
            _visibleGfxObjIds.Clear();
            _poolIndex = 0;
            _cellBatches.Clear();

            foreach (var lb in _landblocks.Values) {
                if (!lb.GpuReady || lb.Instances.Count == 0 || !IsWithinRenderDistance(lb)) continue;

                var testResult = GetLandblockFrustumResult(lb.GridX, lb.GridY);
                if (testResult == FrustumTestResult.Outside) continue;

                foreach (var instance in lb.Instances) {
                    if (testResult == FrustumTestResult.Inside || _frustum.Intersects(instance.BoundingBox)) {
                        var cellId = InstanceIdConstants.GetRawId(instance.InstanceId);
                        if (!_cellBatches.TryGetValue(cellId, out var cellGroups)) {
                            cellGroups = new Dictionary<ulong, List<Matrix4x4>>();
                            _cellBatches[cellId] = cellGroups;
                        }

                        if (instance.IsSetup) {
                            var renderData = MeshManager.TryGetRenderData(instance.ObjectId);
                            if (renderData is { IsSetup: true }) {
                                foreach (var (partId, partTransform) in renderData.SetupParts) {
                                    if (!cellGroups.TryGetValue(partId, out var list)) {
                                        list = GetPooledList();
                                        cellGroups[partId] = list;
                                    }
                                    list.Add(partTransform * instance.Transform);
                                }
                            }
                        }
                        else {
                            if (!cellGroups.TryGetValue(instance.ObjectId, out var list)) {
                                list = GetPooledList();
                                cellGroups[instance.ObjectId] = list;
                            }
                            list.Add(instance.Transform);
                        }
                    }
                }
            }
        }

        public override void Render(int renderPass) {
            Render(renderPass, null);
        }

        public void Render(int renderPass, HashSet<uint>? filter) {
            if (!_initialized || _shader is null || (_shader is GLSLShader glsl && glsl.Program == 0) || (_cameraPosition.Z > 4000 && renderPass != 2)) return;

            CurrentVAO = 0;
            CurrentIBO = 0;
            CurrentAtlas = 0;
            CurrentCullMode = null;

            _shader.SetUniform("uRenderPass", renderPass);

            if (filter == null) {
                foreach (var cellId in _cellBatches.Keys) {
                    RenderCell(cellId);
                }
            }
            else {
                foreach (var cellId in filter) {
                    RenderCell(cellId);
                }
            }

            // Draw highlighted / selected objects on top
            if (RenderHighlightsWhenEmpty || _cellBatches.Count > 0) {
                Gl.DepthFunc(GLEnum.Lequal);
                if (SelectedInstance.HasValue) {
                    RenderSelectedInstance(SelectedInstance.Value, LandscapeColorsSettings.Instance.Selection);
                }
                if (HoveredInstance.HasValue && HoveredInstance != SelectedInstance) {
                    RenderSelectedInstance(HoveredInstance.Value, LandscapeColorsSettings.Instance.Hover);
                }
                Gl.DepthFunc(GLEnum.Less);
            }

            _shader.SetUniform("uHighlightColor", Vector4.Zero);
            _shader.SetUniform("uRenderPass", renderPass);
            Gl.BindVertexArray(0);
        }

        private void RenderCell(uint cellId) {
            if (_cellBatches.TryGetValue(cellId, out var groups)) {
                foreach (var (gfxObjId, transforms) in groups) {
                    var renderData = MeshManager.TryGetRenderData(gfxObjId);
                    if (renderData != null && !renderData.IsSetup) {
                        RenderObjectBatches(_shader!, renderData, transforms);
                    }
                }
            }
        }

        protected override void PopulatePartGroups(ObjectLandblock lb, List<SceneryInstance> instances) {
            lb.BuildingPartGroups.Clear(); // Using BuildingPartGroups for EnvCell parts
            foreach (var instance in instances) {
                var targetGroup = lb.BuildingPartGroups;
                if (instance.IsSetup) {
                    var renderData = MeshManager.TryGetRenderData(instance.ObjectId);
                    if (renderData is { IsSetup: true }) {
                        foreach (var (partId, partTransform) in renderData.SetupParts) {
                            if (!targetGroup.TryGetValue(partId, out var list)) {
                                list = new List<Matrix4x4>();
                                targetGroup[partId] = list;
                            }
                            list.Add(partTransform * instance.Transform);
                        }
                    }
                }
                else {
                    if (!targetGroup.TryGetValue(instance.ObjectId, out var list)) {
                        list = new List<Matrix4x4>();
                        targetGroup[instance.ObjectId] = list;
                    }
                    list.Add(instance.Transform);
                }
            }
        }

        protected override void OnUnloadResources(ObjectLandblock lb, ushort key) {
            lock (_tcsLock) {
                _instanceReadyTcs.TryRemove(key, out _);
                lb.InstancesReady = false;
            }
        }

        protected override void OnInvalidateLandblock(ushort key) {
            lock (_tcsLock) {
                _instanceReadyTcs.TryRemove(key, out _);
                if (_landblocks.TryGetValue(key, out var lb)) {
                    lb.InstancesReady = false;
                }
            }
        }

        protected override void OnLandblockChangedExtra(ushort key) {
            lock (_tcsLock) {
                _instanceReadyTcs.TryRemove(key, out _);
            }
        }

        protected override async Task GenerateForLandblockAsync(ObjectLandblock lb, CancellationToken ct) {
            try {
                var key = GeometryUtils.PackKey(lb.GridX, lb.GridY);
                if (!IsWithinRenderDistance(lb) || !_landblocks.ContainsKey(key)) return;
                ct.ThrowIfCancellationRequested();

                if (LandscapeDoc.Region is not ITerrainInfo regionInfo) return;

                var lbGlobalX = (uint)lb.GridX;
                var lbGlobalY = (uint)lb.GridY;

                // LandBlockInfo ID: high byte = X, next byte = Y, low word = 0xFFFE
                var lbId = (lbGlobalX << 8 | lbGlobalY) << 16 | 0xFFFE;

                var instances = new List<SceneryInstance>();
                var lbSizeUnits = regionInfo.LandblockSizeInUnits; // 192

                var mergedLb = LandscapeDoc.GetMergedLandblock(lbId);

                // Find entry portals from buildings in this landblock
                var discoveredCellIds = new HashSet<uint>();
                var entryCellIds = new HashSet<uint>();
                var cellsToProcess = new Queue<uint>();

                var cellDb = LandscapeDoc.CellDatabase;
                if (cellDb != null && mergedLb.Buildings.Count > 0) {
                    if (cellDb.TryGet<LandBlockInfo>(lbId, out var lbi)) {
                        foreach (var building in mergedLb.Buildings) {
                            int index = (int)InstanceIdConstants.GetRawId(building.InstanceId);
                            if (index < lbi.Buildings.Count) {
                                var bInfo = lbi.Buildings[index];
                                // Start discovery from building portals
                                foreach (var portal in bInfo.Portals) {
                                    if (portal.OtherCellId != 0xFFFF) {
                                        var cellId = (lbId & 0xFFFF0000) | portal.OtherCellId;
                                        if (discoveredCellIds.Add(cellId)) {
                                            entryCellIds.Add(cellId);
                                            cellsToProcess.Enqueue(cellId);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else {
                        Log.LogWarning("Failed to get LandBlockInfo for {LbId:X8}", lbId);
                    }
                }

                uint numVisibleCells = 0;

                // Recursively gather connected EnvCells
                while (cellsToProcess.Count > 0) {
                    var cellId = cellsToProcess.Dequeue();

                    if (cellDb != null && cellDb.TryGet<EnvCell>(cellId, out var envCell)) {
                        // We always add the cell to instances so GetEnvCellAt can find it,
                        // even if it's not marked SeenOutside. Portal-based rendering
                        // will handle occluding it if it's not visible.
                        numVisibleCells++;

                        // Calculate world position
                        var datPos = new Vector3((float)envCell.Position.Origin.X, (float)envCell.Position.Origin.Y, (float)envCell.Position.Origin.Z);
                        var worldPos = new Vector3(
                            new Vector2(lbGlobalX * lbSizeUnits + datPos.X, lbGlobalY * lbSizeUnits + datPos.Y) + regionInfo.MapOffset,
                            datPos.Z + RenderConstants.ObjectZOffset
                        );

                        var rotation = new System.Numerics.Quaternion(
                            (float)envCell.Position.Orientation.X,
                            (float)envCell.Position.Orientation.Y,
                            (float)envCell.Position.Orientation.Z,
                            (float)envCell.Position.Orientation.W
                        );

                        var transform = Matrix4x4.CreateFromQuaternion(rotation)
                            * Matrix4x4.CreateTranslation(worldPos);

                        // Add the cell geometry itself
                        uint envId = 0x0D000000u | envCell.EnvironmentId;
                        if (_dats.Portal.TryGet<DatReaderWriter.DBObjs.Environment>(envId, out var environment)) {
                            if (environment.Cells.TryGetValue(envCell.CellStructure, out var cellStruct)) {
                                // Use synthetic ID for cell geometry (bit 32 set)
                                var cellGeomId = (ulong)cellId | 0x1_0000_0000UL;
                                var bounds = MeshManager.GetBounds(cellGeomId, false);
                                var localBbox = bounds.HasValue ? new BoundingBox(bounds.Value.Min, bounds.Value.Max) : default;
                                var bbox = localBbox.Transform(transform);

                                instances.Add(new SceneryInstance {
                                    ObjectId = cellGeomId,
                                    InstanceId = InstanceIdConstants.Encode(cellId, InspectorSelectionType.EnvCell),
                                    IsSetup = false,
                                    IsBuilding = true,
                                    IsEntryCell = entryCellIds.Contains(cellId),
                                    WorldPosition = worldPos,
                                    LocalPosition = datPos,
                                    Rotation = rotation,
                                    Scale = Vector3.One,
                                    Transform = transform,
                                    LocalBoundingBox = localBbox,
                                    BoundingBox = bbox
                                });
                            }
                        }

                        // Add static objects within the cell
                        if (envCell.StaticObjects.Count > 0) {
                            for (ushort i = 0; i < envCell.StaticObjects.Count; i++) {
                                var stab = envCell.StaticObjects[i];

                                var datStabPos = new Vector3((float)stab.Frame.Origin.X, (float)stab.Frame.Origin.Y, (float)stab.Frame.Origin.Z);
                                var stabWorldPos = new Vector3(
                                    new Vector2(lbGlobalX * lbSizeUnits + datStabPos.X, lbGlobalY * lbSizeUnits + datStabPos.Y) + regionInfo.MapOffset,
                                    datStabPos.Z + RenderConstants.ObjectZOffset
                                );

                                var stabWorldRot = new System.Numerics.Quaternion(
                                    (float)stab.Frame.Orientation.X,
                                    (float)stab.Frame.Orientation.Y,
                                    (float)stab.Frame.Orientation.Z,
                                    (float)stab.Frame.Orientation.W
                                );
                                var stabWorldTransform = Matrix4x4.CreateFromQuaternion(stabWorldRot) * Matrix4x4.CreateTranslation(stabWorldPos);

                                var isSetup = (stab.Id >> 24) == 0x02;
                                var bounds = MeshManager.GetBounds(stab.Id, isSetup);
                                var localBbox = bounds.HasValue ? new BoundingBox(bounds.Value.Min, bounds.Value.Max) : default;
                                var bbox = localBbox.Transform(stabWorldTransform);

                                instances.Add(new SceneryInstance {
                                    ObjectId = stab.Id,
                                    InstanceId = InstanceIdConstants.EncodeEnvCellStaticObject(cellId, i, false),
                                    IsSetup = isSetup,
                                    IsBuilding = false,
                                    WorldPosition = stabWorldPos,
                                    LocalPosition = datStabPos,
                                    Rotation = stabWorldRot,
                                    Scale = Vector3.One,
                                    Transform = stabWorldTransform,
                                    LocalBoundingBox = localBbox,
                                    BoundingBox = bbox
                                });
                            }
                        }

                        // Recursively walk portals to other interior cells
                        foreach (var portal in envCell.CellPortals) {
                            if (portal.OtherCellId != 0xFFFF) {
                                var neighborId = (lbId & 0xFFFF0000) | portal.OtherCellId;
                                if (discoveredCellIds.Add(neighborId)) {
                                    cellsToProcess.Enqueue(neighborId);
                                }
                            }
                        }
                    }
                }

                lb.PendingInstances = instances;

                lock (_tcsLock) {
                    lb.InstancesReady = true;
                    if (_instanceReadyTcs.TryGetValue(key, out var tcs)) {
                        tcs.TrySetResult();
                    }
                }

                // Prepare mesh data for unique objects on background thread
                await PrepareMeshesForInstances(instances, ct);

                lb.MeshDataReady = true;
                _uploadQueue.Enqueue(lb);
            }
            catch (OperationCanceledException) {
                // Ignore cancellations
            }
            catch (Exception ex) {
                Log.LogError(ex, "Error generating EnvCells for landblock ({X},{Y})", lb.GridX, lb.GridY);
            }
        }

        #endregion
    }
}
