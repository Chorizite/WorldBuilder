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
using WorldBuilder.Shared.Services;
using BoundingBox = Chorizite.Core.Lib.BoundingBox;
using System.Runtime.InteropServices;

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

        protected override bool RenderHighlightsWhenEmpty => true;

        protected override int MaxConcurrentGenerations => Math.Max(4, System.Environment.ProcessorCount * 2);

        protected override bool UseInstanceBuffer => false;

        public EnvCellRenderManager(GL gl, ILogger log, LandscapeDocument landscapeDoc,
            IDatReaderWriter dats, OpenGLGraphicsDevice graphicsDevice, ObjectMeshManager meshManager, Frustum frustum)
            : base(gl, graphicsDevice, meshManager, log, landscapeDoc, frustum, false, 1024) {
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

        public virtual uint GetEnvCellAt(Vector3 pos, bool onlyEntryCells = false) {
            if (LandscapeDoc.Region == null) return 0;

            var lbSize = LandscapeDoc.Region.LandblockSizeInUnits;
            var mapPos = new Vector2(pos.X, pos.Y) - LandscapeDoc.Region.MapOffset;
            int lbX = (int)Math.Floor(mapPos.X / lbSize);
            int lbY = (int)Math.Floor(mapPos.Y / lbSize);

            // Is the current XY position outside the map?
            if (lbX < 0 || lbY < 0 || lbX >= LandscapeDoc.Region.MapWidthInLandblocks || lbY >= LandscapeDoc.Region.MapHeightInLandblocks) return 0;

            // Only check landblocks in a 3x3 neighborhood of the position.
            // Most EnvCells are within their originating landblock or its immediate neighbors.
            for (int x = lbX - 1; x <= lbX + 1; x++) {
                for (int y = lbY - 1; y <= lbY + 1; y++) {
                    var key = GeometryUtils.PackKey(x, y);
                    if (!_landblocks.TryGetValue(key, out var lb) || !lb.InstancesReady) continue;

                    // Broad-phase: check total EnvCell bounds for this landblock
                    if (pos.X < lb.TotalEnvCellBounds.Min.X - 1f || pos.X > lb.TotalEnvCellBounds.Max.X + 1f ||
                        pos.Y < lb.TotalEnvCellBounds.Min.Y - 1f || pos.Y > lb.TotalEnvCellBounds.Max.Y + 1f ||
                        pos.Z < lb.TotalEnvCellBounds.Min.Z - 1f || pos.Z > lb.TotalEnvCellBounds.Max.Z + 1f) {
                        continue;
                    }

                    lock (lb) {
                        if (CheckInstances(lb.Instances, pos, onlyEntryCells, out var cellId)) return cellId;
                        if (lb.PendingInstances != null && CheckInstances(lb.PendingInstances, pos, onlyEntryCells, out cellId)) return cellId;
                    }
                }
            }

            return 0; // Definitely not in an EnvCell in this loaded area
        }

        public static ulong GetEnvCellGeomId(uint environmentId, ushort cellStructure, List<ushort> surfaces) {
            var hash = 17L;
            hash = hash * 31 + (int)environmentId;
            hash = hash * 31 + cellStructure;
            foreach (var surface in surfaces) {
                hash = hash * 31 + surface;
            }
            // Use bit 33 to indicate deduplicated EnvCell geometry (to avoid collision with bit 32 per-cell geometry)
            return (ulong)hash | 0x2_0000_0000UL;
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

        public virtual bool Raycast(Vector3 rayOrigin, Vector3 rayDirection, bool includeCells, bool includeStaticObjects, out SceneRaycastHit hit, uint currentCellId = 0, bool isCollision = false, float maxDistance = float.MaxValue, ulong ignoreInstanceId = 0) {
            hit = SceneRaycastHit.NoHit;

            if (!isCollision && !_showEnvCells) return false;

            // Early exit: Don't collide with interiors if we are outside
            if (isCollision && currentCellId == 0) return false;

            ushort? targetLbKey = currentCellId != 0 ? (ushort)(currentCellId >> 16) : null;

            foreach (var (key, lb) in _landblocks) {
                if (!lb.InstancesReady) continue;

                // If we know which landblock we are in, only check that one
                if (targetLbKey.HasValue && key != targetLbKey.Value) continue;

                lock (lb) {
                    foreach (var instance in lb.Instances) {
                        if (ignoreInstanceId != 0 && instance.InstanceId == ignoreInstanceId) continue;

                        var type = InstanceIdConstants.GetType(instance.InstanceId);
                        if (type == InspectorSelectionType.EnvCell && !includeCells) continue;
                        if (type == InspectorSelectionType.EnvCellStaticObject && !includeStaticObjects) continue;

                        var renderData = MeshManager.TryGetRenderData(instance.ObjectId);
                        if (renderData == null) continue;

                        // Broad phase: Bounding Box
                        if (instance.BoundingBox.Max != instance.BoundingBox.Min) {
                            if (!GeometryUtils.RayIntersectsBox(rayOrigin, rayDirection, instance.BoundingBox.Min, instance.BoundingBox.Max, out float boxDist)) {
                                continue;
                            }
                            if (boxDist > maxDistance) {
                                continue;
                            }
                        }

                        // Narrow phase: Mesh-precise raycast
                        if (MeshManager.IntersectMesh(renderData, instance.Transform, rayOrigin, rayDirection, out float d, out Vector3 normal)) {
                            if (d < hit.Distance && d <= maxDistance) {
                                hit.Hit = true;
                                hit.Distance = d;
                                hit.Type = type;
                                if (type == InspectorSelectionType.EnvCell) {
                                    hit.ObjectId = InstanceIdConstants.GetRawId(instance.InstanceId);
                                }
                                else {
                                    hit.ObjectId = (uint)instance.ObjectId;
                                }
                                hit.InstanceId = instance.InstanceId;
                                hit.SecondaryId = InstanceIdConstants.GetSecondaryId(instance.InstanceId);
                                hit.Position = rayOrigin + rayDirection * d;
                                hit.LocalPosition = instance.LocalPosition;
                                hit.Rotation = instance.Rotation;
                                hit.LandblockId = (uint)key << 16 | 0xFFFE;
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
                if (_frustum.TestBox(lb.TotalEnvCellBounds) == FrustumTestResult.Outside) continue;

                lock (lb) {
                    foreach (var instance in lb.Instances) {
                        // Skip cell geometry itself
                        if (instance.IsBuilding) continue;
                        if (!settings.SelectStaticObjects) continue;

                        // Skip if instance is outside frustum
                        if (!_frustum.Intersects(instance.BoundingBox)) continue;

                        var isSelected = SelectedInstance.HasValue && SelectedInstance.Value.LandblockKey == GeometryUtils.PackKey(lb.GridX, lb.GridY) && SelectedInstance.Value.InstanceId == instance.InstanceId;
                        var isHovered = HoveredInstance.HasValue && HoveredInstance.Value.LandblockKey == GeometryUtils.PackKey(lb.GridX, lb.GridY) && HoveredInstance.Value.InstanceId == instance.InstanceId;

                        Vector4 color;
                        if (isSelected) color = LandscapeColorsSettings.Instance.Selection;
                        else if (isHovered) color = LandscapeColorsSettings.Instance.Hover;
                        else color = settings.StaticObjectColor;

                        debug.DrawBox(instance.LocalBoundingBox.ToShared(), instance.Transform, color);
                    }
                }
            }
        }

        #endregion

        #region Protected: Overrides

        public override void UpdateInstanceTransform(uint landblockId, ulong instanceId, Vector3 position, Quaternion rotation, uint currentCellId = 0) {
            var type = InstanceIdConstants.GetType(instanceId);
            if (type == InspectorSelectionType.EnvCellStaticObject || type == InspectorSelectionType.EnvCell) {
                ushort key = (ushort)(landblockId >> 16);
                if (key == 0 || !_landblocks.ContainsKey(key)) {
                    foreach (var (lbKey, lb) in _landblocks) {
                        lock (lb) {
                            for (int i = 0; i < lb.Instances.Count; i++) {
                                if (lb.Instances[i].InstanceId == instanceId) {
                                    base.UpdateInstanceTransform((uint)lbKey << 16 | 0xFFFE, instanceId, position, rotation, currentCellId);
                                    return;
                                }
                            }
                        }
                    }
                }
            }
            base.UpdateInstanceTransform(landblockId, instanceId, position, rotation, currentCellId);
        }

        public override void PrepareRenderBatches(Matrix4x4 viewProjectionMatrix, Vector3 cameraPosition, HashSet<uint>? filter = null, bool isOutside = false) {

            if (!_initialized || cameraPosition.Z > 4000) return;

            lock (_renderLock) {
                _poolIndex = 0;
            }

            if (LandscapeDoc.Region != null) {
                var lbSize = LandscapeDoc.Region.CellSizeInUnits * LandscapeDoc.Region.LandblockCellLength;
                var pos = new Vector2(cameraPosition.X, cameraPosition.Y) - LandscapeDoc.Region.MapOffset;
                _cameraLbX = (int)Math.Floor(pos.X / lbSize);
                _cameraLbY = (int)Math.Floor(pos.Y / lbSize);
            }

            var landblocks = _landblocks.Values.Where(lb => lb.GpuReady && lb.Instances.Count > 0 && IsWithinRenderDistance(lb)).ToList();
            if (landblocks.Count == 0) return;

            // Use ThreadLocal to avoid contention on ConcurrentDictionaries during parallel grouping
            using var threadLocalBatchedByCell = new ThreadLocal<Dictionary<uint, Dictionary<ulong, List<InstanceData>>>>(() => new(), true);
            using var threadLocalGlobalGroups = new ThreadLocal<Dictionary<ulong, List<InstanceData>>>(() => new(), true);

            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = System.Environment.ProcessorCount };
            Parallel.ForEach(landblocks, parallelOptions, lb => {
                lock (lb) {
                    var testResult = _frustum.TestBox(lb.TotalEnvCellBounds);
                    if (testResult == FrustumTestResult.Outside) return;

                    var lbBatchedByCell = threadLocalBatchedByCell.Value!;
                    var lbGlobalGroups = threadLocalGlobalGroups.Value!;

                    // Fast path: Landblock is fully inside frustum
                    if (testResult == FrustumTestResult.Inside) {
                        foreach (var (gfxObjId, instances) in lb.BuildingPartGroups) {
                            foreach (var instanceData in instances) {
                                if (filter != null && !filter.Contains(instanceData.CellId)) continue;

                                AddToGroups(lbBatchedByCell, lbGlobalGroups, instanceData.CellId, gfxObjId, instanceData);
                            }
                        }
                        return;
                    }

                    // Slow path: Test each cell individually using EnvCellBounds
                    var visibleCells = new HashSet<uint>();
                    foreach (var kvp in lb.EnvCellBounds) {
                        var cellId = kvp.Key;
                        if (filter != null && !filter.Contains(cellId)) continue;

                        if (_frustum.Intersects(kvp.Value)) {
                            visibleCells.Add(cellId);
                        }
                    }

                    if (visibleCells.Count > 0) {
                        foreach (var (gfxObjId, instances) in lb.BuildingPartGroups) {
                            foreach (var instanceData in instances) {
                                if (visibleCells.Contains(instanceData.CellId)) {
                                    AddToGroups(lbBatchedByCell, lbGlobalGroups, instanceData.CellId, gfxObjId, instanceData);
                                }
                            }
                        }
                    }
                }
            });

            // Rebuild final collections locally
            var newBatchedByCell = new Dictionary<uint, Dictionary<ulong, List<InstanceData>>>();
            var newVisibleGroups = new Dictionary<ulong, List<InstanceData>>();
            var newVisibleGfxObjIds = new List<ulong>();

            // Merge results from all threads
            foreach (var localBatchedByCell in threadLocalBatchedByCell.Values) {
                foreach (var cellKvp in localBatchedByCell) {
                    if (!newBatchedByCell.TryGetValue(cellKvp.Key, out var gfxDict)) {
                        gfxDict = new Dictionary<ulong, List<InstanceData>>();
                        newBatchedByCell[cellKvp.Key] = gfxDict;
                    }
                    foreach (var gfxKvp in cellKvp.Value) {
                        if (!gfxDict.TryGetValue(gfxKvp.Key, out var list)) {
                            list = GetPooledList();
                            gfxDict[gfxKvp.Key] = list;
                        }
                        list.AddRange(gfxKvp.Value);
                    }
                }
            }

            foreach (var localGlobalGroups in threadLocalGlobalGroups.Values) {
                foreach (var kvp in localGlobalGroups) {
                    if (!newVisibleGroups.TryGetValue(kvp.Key, out var list)) {
                        list = GetPooledList();
                        newVisibleGroups[kvp.Key] = list;
                        newVisibleGfxObjIds.Add(kvp.Key);
                    }
                    list.AddRange(kvp.Value);
                }
            }

            // Atomic swap under lock
            lock (_renderLock) {
                _activeSnapshot = new VisibilitySnapshot {
                    BatchedByCell = newBatchedByCell,
                    VisibleGroups = newVisibleGroups,
                    VisibleGfxObjIds = newVisibleGfxObjIds,
                    VisibleLandblocks = new List<ObjectLandblock>(), // EnvCells don't use consolidated MDI yet
                    PostPreparePoolIndex = _poolIndex
                };
                _poolIndex = 0;
                NeedsPrepare = false;
                MarkMdiDirty();
            }
        }

        private static void AddToGroups(Dictionary<uint, Dictionary<ulong, List<InstanceData>>> batchedByCell, Dictionary<ulong, List<InstanceData>> globalGroups, uint cellId, ulong gfxObjId, InstanceData data) {
            // Add to global grouping
            if (!globalGroups.TryGetValue(gfxObjId, out var globalList)) {
                globalList = new List<InstanceData>();
                globalGroups[gfxObjId] = globalList;
            }
            globalList.Add(data);

            // Add to per-cell grouping
            if (!batchedByCell.TryGetValue(cellId, out var gfxDict)) {
                gfxDict = new Dictionary<ulong, List<InstanceData>>();
                batchedByCell[cellId] = gfxDict;
            }
            if (!gfxDict.TryGetValue(gfxObjId, out var list)) {
                list = new List<InstanceData>();
                batchedByCell[cellId][gfxObjId] = list;
            }
            list.Add(data);
        }

        public override void Render(RenderPass renderPass) {
            lock (_renderLock) {
                Render(renderPass, null);
            }
        }

        public unsafe void Render(RenderPass renderPass, HashSet<uint>? filter) {
            if (!_initialized || _shader is null || (_shader is GLSLShader glsl && glsl.Program == 0) || (_cameraPosition.Z > 4000 && renderPass != RenderPass.SinglePass)) return;

            lock (_renderLock) {
                var snapshot = _activeSnapshot;
                _shader.Bind();
                _poolIndex = snapshot.PostPreparePoolIndex;
                CurrentVAO = 0;
                CurrentIBO = 0;
                CurrentAtlas = 0;
                CurrentInstanceBuffer = 0;
                CurrentCullMode = null;

                _shader.SetUniform("uRenderPass", (int)renderPass);
                _shader.SetUniform("uFilterByCell", 0);

                var allInstances = new List<InstanceData>();
                var drawCalls = new List<(ObjectRenderData renderData, int count, int offset)>();

                if (filter == null) {
                    // Optimized path: Use global groups batched across all cells
                    foreach (var gfxObjId in snapshot.VisibleGfxObjIds) {
                        if (snapshot.VisibleGroups.TryGetValue(gfxObjId, out var transforms)) {
                            var renderData = MeshManager.TryGetRenderData(gfxObjId);
                            if (renderData != null && !renderData.IsSetup) {
                                drawCalls.Add((renderData, transforms.Count, allInstances.Count));
                                allInstances.AddRange(transforms);
                            }
                        }
                    }
                }
                else {
                    // Group by gfxObjId within the filtered cells to minimize draw calls
                    var filteredGroups = new Dictionary<ulong, List<InstanceData>>();
                    var ownedLists = new HashSet<List<InstanceData>>();

                    foreach (var cellId in filter) {
                        if (snapshot.BatchedByCell.TryGetValue(cellId, out var gfxDict)) {
                            foreach (var (gfxObjId, transforms) in gfxDict) {
                                if (transforms.Count > 0) {
                                    if (!filteredGroups.TryGetValue(gfxObjId, out var list)) {
                                        list = transforms; // Optimization: just use the first list
                                        filteredGroups[gfxObjId] = list;
                                    }
                                    else {
                                        if (list == transforms) continue;

                                        // If we don't own this list yet, we must clone it before adding to it
                                        if (!ownedLists.Contains(list)) {
                                            var newList = GetPooledList();
                                            newList.AddRange(list);
                                            list = newList;
                                            filteredGroups[gfxObjId] = list;
                                            ownedLists.Add(list);
                                        }
                                        list.AddRange(transforms);
                                    }
                                }
                            }
                        }
                    }

                    foreach (var (gfxObjId, transforms) in filteredGroups) {
                        var renderData = MeshManager.TryGetRenderData(gfxObjId);
                        if (renderData != null && !renderData.IsSetup) {
                            drawCalls.Add((renderData, transforms.Count, allInstances.Count));
                            allInstances.AddRange(transforms);
                        }
                    }
                }

                if (allInstances.Count > 0) {
                    if (_useModernRendering) {
                        RenderModernMDI(_shader, drawCalls, allInstances, renderPass);
                    }
                    else {
                        // Upload all instance data in one go (with orphaning)
                        GraphicsDevice.UpdateInstanceBuffer(allInstances);

                        // Issue draw calls
                        foreach (var call in drawCalls) {
                            RenderObjectBatches(_shader!, call.renderData, call.count, call.offset, renderPass);
                        }
                    }
                }

                // Draw highlighted / selected objects on top
                if (RenderHighlightsWhenEmpty || snapshot.BatchedByCell.Count > 0) {
                    Gl.Enable(EnableCap.PolygonOffsetFill);
                    Gl.PolygonOffset(-1.0f, -1.0f);
                    Gl.DepthFunc(GLEnum.Lequal);
                    if (SelectedInstance.HasValue) {
                        var type = InstanceIdConstants.GetType(SelectedInstance.Value.InstanceId);
                        if (type == InspectorSelectionType.EnvCell) {
                            RenderSelectedInstance(SelectedInstance.Value, LandscapeColorsSettings.Instance.EnvCellSelection, renderPass);
                        }
                    }
                    if (HoveredInstance.HasValue && HoveredInstance != SelectedInstance) {
                        var type = InstanceIdConstants.GetType(HoveredInstance.Value.InstanceId);
                        if (type == InspectorSelectionType.EnvCell) {
                            RenderSelectedInstance(HoveredInstance.Value, LandscapeColorsSettings.Instance.EnvCellHover, renderPass);
                        }
                    }
                    Gl.DepthFunc(GLEnum.Less);
                    Gl.Disable(EnableCap.PolygonOffsetFill);
                }

                _shader.SetUniform("uHighlightColor", Vector4.Zero);
                _shader.SetUniform("uRenderPass", (int)renderPass);
                Gl.BindVertexArray(0);
                CurrentVAO = 0;
            }
        }

        public override void RenderHighlight(RenderPass renderPass, IShader? shader = null, Vector4? color = null, float outlineWidth = 1.0f, bool selected = true, bool hovered = true) {
            lock (_renderLock) {
                var currentShader = shader ?? _shader!;
                if (currentShader == null || currentShader.ProgramId == 0) return;

                currentShader.Bind();
                currentShader.SetUniform("uRenderPass", (int)renderPass);
                currentShader.SetUniform("uOutlineWidth", outlineWidth);

                if (selected && SelectedInstance.HasValue) {
                    var type = InstanceIdConstants.GetType(SelectedInstance.Value.InstanceId);
                    if (type == InspectorSelectionType.EnvCellStaticObject) {
                        RenderSelectedInstance(SelectedInstance.Value, color ?? LandscapeColorsSettings.Instance.Selection, renderPass, currentShader);
                    }
                }
                if (hovered && HoveredInstance.HasValue && HoveredInstance != SelectedInstance) {
                    var type = InstanceIdConstants.GetType(HoveredInstance.Value.InstanceId);
                    if (type == InspectorSelectionType.EnvCellStaticObject) {
                        RenderSelectedInstance(HoveredInstance.Value, color ?? LandscapeColorsSettings.Instance.Hover, renderPass, currentShader);
                    }
                }

                currentShader.SetUniform("uHighlightColor", Vector4.Zero);
                Gl.BindVertexArray(0);
                CurrentVAO = 0;
            }
        }

        protected override void PopulatePartGroups(ObjectLandblock lb, List<SceneryInstance> instances) {
            lb.BuildingPartGroups.Clear(); // Using BuildingPartGroups for EnvCell parts
            foreach (var instance in instances) {
                var targetGroup = lb.BuildingPartGroups;
                var cellId = instance.CurrentPreviewCellId != 0 ? instance.CurrentPreviewCellId : InstanceIdConstants.GetRawId(instance.InstanceId);
                if (instance.IsSetup) {
                    var renderData = MeshManager.TryGetRenderData(instance.ObjectId);
                    if (renderData is { IsSetup: true }) {
                        foreach (var (partId, partTransform) in renderData.SetupParts) {
                            if (!targetGroup.TryGetValue(partId, out var list)) {
                                list = new List<InstanceData>();
                                targetGroup[partId] = list;
                            }
                            list.Add(new InstanceData { Transform = partTransform * instance.Transform, CellId = cellId });
                        }
                    }
                }
                else {
                    if (!targetGroup.TryGetValue(instance.ObjectId, out var list)) {
                        list = new List<InstanceData>();
                        targetGroup[instance.ObjectId] = list;
                    }
                    list.Add(new InstanceData { Transform = instance.Transform, CellId = cellId });
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
            }
        }

        protected override void OnLandblockChangedExtra(ushort key) {
            lock (_tcsLock) {
                _instanceReadyTcs.TryRemove(key, out _);
            }
        }

        private readonly ConcurrentDictionary<uint, bool> _hasBuildingsCache = new();

        protected override float GetPriority(ObjectLandblock lb, Vector2 camDir2D, int cameraLbX, int cameraLbY) {
            var priority = base.GetPriority(lb, camDir2D, cameraLbX, cameraLbY);

            // Prioritize landblocks with buildings (since they contain EnvCells)
            var lbId = ((uint)lb.GridX << 8 | (uint)lb.GridY) << 16 | 0xFFFE;
            if (!_hasBuildingsCache.TryGetValue(lbId, out var hasBuildings)) {
                var mergedLb = LandscapeDoc.GetMergedLandblock(lbId);
                hasBuildings = mergedLb.Buildings.Count > 0;
                if (hasBuildings) {
                    _hasBuildingsCache[lbId] = hasBuildings;
                }
            }

            if (hasBuildings) {
                priority -= 10f; // Bonus for having buildings
            }

            return priority;
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

                var mergedLb = await LandscapeDoc.GetMergedLandblockAsync(lbId);

                // Find entry portals from buildings in this landblock
                var discoveredCellIds = new HashSet<uint>();
                var entryCellIds = new HashSet<uint>();
                var cellsToProcess = new Queue<uint>();
                var envCellBounds = new Dictionary<uint, BoundingBox>();
                var seenOutsideCells = new HashSet<uint>();
                var cellGeomIdToEnvCell = new Dictionary<ulong, (uint envId, ushort cellStruct, List<ushort> surfaces)>();

                var cellDb = LandscapeDoc.CellDatabase;
                if (cellDb != null && mergedLb.Buildings.Count > 0) {
                    if (cellDb.TryGet<LandBlockInfo>(lbId, out var lbi)) {
                        foreach (var building in mergedLb.Buildings.Values) {
                            int index = InstanceIdConstants.GetObjectIndex(building.InstanceId);
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
                    var envCell = await LandscapeDoc.GetMergedEnvCellAsync(cellId);

                    if (envCell.EnvironmentId != 0) {
                        // We always add the cell to instances so GetEnvCellAt can find it,
                        // even if it's not marked SeenOutside. Portal-based rendering
                        // will handle occluding it if it's not visible.
                        numVisibleCells++;

                        if ((envCell.Flags & (uint)EnvCellFlags.SeenOutside) != 0) {
                            seenOutsideCells.Add(cellId);
                        }

                        // Calculate world position
                        var datPos = envCell.Position;
                        var worldPos = new Vector3(
                            new Vector2(lbGlobalX * lbSizeUnits + datPos.X, lbGlobalY * lbSizeUnits + datPos.Y) + regionInfo.MapOffset,
                            datPos.Z
                        );

                        var rotation = envCell.Rotation;

                        var transform = Matrix4x4.CreateFromQuaternion(rotation)
                            * Matrix4x4.CreateTranslation(worldPos);

                        // Add the cell geometry itself
                        uint envId = 0x0D000000u | envCell.EnvironmentId;
                        if (_dats.Portal.TryGet<DatReaderWriter.DBObjs.Environment>(envId, out var environment)) {
                            if (environment.Cells.TryGetValue(envCell.CellStructure, out var cellStruct)) {
                                // Use deduplicated ID for cell geometry
                                var cellGeomId = GetEnvCellGeomId(envCell.EnvironmentId, envCell.CellStructure, envCell.Surfaces);
                                // Store enough data for mesh generation if needed
                                cellGeomIdToEnvCell[cellGeomId] = (envCell.EnvironmentId, envCell.CellStructure, envCell.Surfaces);
                                var bounds = MeshManager.GetBounds(cellGeomId, false);
                                if (!bounds.HasValue) {
                                    // Fallback: if bounds not cached for deduplicated ID, use the EnvCell ID to find them
                                    bounds = MeshManager.GetBounds(cellId, false);
                                }
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

                                envCellBounds[cellId] = bbox;
                            }
                        }

                        // Add static objects within the cell
                        if (envCell.StaticObjects.Count > 0) {
                            foreach (var stab in envCell.StaticObjects.Values) {
                                var datStabPos = stab.Position;
                                var stabWorldPos = new Vector3(
                                    new Vector2(lbGlobalX * lbSizeUnits + datStabPos.X, lbGlobalY * lbSizeUnits + datStabPos.Y) + regionInfo.MapOffset,
                                    datStabPos.Z
                                );

                                var stabWorldRot = stab.Rotation;
                                var stabWorldTransform = Matrix4x4.CreateFromQuaternion(stabWorldRot) * Matrix4x4.CreateTranslation(stabWorldPos);

                                var isSetup = (stab.SetupId >> 24) == 0x02;
                                var bounds = MeshManager.GetBounds(stab.SetupId, isSetup);
                                var localBbox = bounds.HasValue ? new BoundingBox(bounds.Value.Min, bounds.Value.Max) : default;
                                var bbox = localBbox.Transform(stabWorldTransform);

                                instances.Add(new SceneryInstance {
                                    ObjectId = stab.SetupId,
                                    InstanceId = stab.InstanceId,
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

                                if (envCellBounds.TryGetValue(cellId, out var currentBox)) {
                                    envCellBounds[cellId] = currentBox.Union(bbox);
                                }
                                else {
                                    envCellBounds[cellId] = bbox;
                                }
                            }
                        }

                        // Recursively walk portals to other interior cells
                        foreach (var portal in envCell.Portals) {
                            if (portal.OtherCellId != 0xFFFF) {
                                var neighborId = (lbId & 0xFFFF0000) | portal.OtherCellId;
                                if (discoveredCellIds.Add(neighborId)) {
                                    cellsToProcess.Enqueue(neighborId);
                                }
                            }
                        }
                    }
                }

                var totalEnvCellBounds = new BoundingBox(new Vector3(float.MaxValue), new Vector3(float.MinValue));
                foreach (var box in envCellBounds.Values) {
                    totalEnvCellBounds = totalEnvCellBounds.Union(box);
                }

                lb.PendingInstances = instances;
                lb.PendingEnvCellBounds = envCellBounds;
                lb.PendingSeenOutsideCells = seenOutsideCells;
                lb.PendingTotalEnvCellBounds = totalEnvCellBounds;

                lock (_tcsLock) {
                    lb.InstancesReady = true;
                    if (_instanceReadyTcs.TryGetValue(key, out var tcs)) {
                        tcs.TrySetResult();
                    }
                }

                // Prepare mesh data for unique objects on background thread
                var uniqueObjects = instances.Select(s => (s.ObjectId, s.IsSetup))
                    .Distinct()
                    .ToList();

                var preparationTasks = new List<Task<ObjectMeshData?>>();
                foreach (var (objectId, isSetup) in uniqueObjects) {
                    if (MeshManager.HasRenderData(objectId) || _preparedMeshes.ContainsKey(objectId))
                        continue;

                    if (cellGeomIdToEnvCell.TryGetValue(objectId, out var cellInfo)) {
                        preparationTasks.Add(MeshManager.PrepareEnvCellGeomMeshDataAsync(objectId, cellInfo.envId, cellInfo.cellStruct, cellInfo.surfaces, ct));
                    }
                    else {
                        preparationTasks.Add(MeshManager.PrepareMeshDataAsync(objectId, isSetup, ct));
                    }
                }

                var preparedMeshes = await Task.WhenAll(preparationTasks);
                foreach (var meshData in preparedMeshes) {
                    if (meshData == null) continue;

                    _preparedMeshes.TryAdd(meshData.ObjectId, meshData);

                    // For Setup objects, also prepare each part's GfxObj
                    if (meshData.IsSetup && meshData.SetupParts.Count > 0) {
                        var partTasks = new List<Task<ObjectMeshData?>>();
                        foreach (var (partId, _) in meshData.SetupParts) {
                            if (!MeshManager.HasRenderData(partId) && !_preparedMeshes.ContainsKey(partId)) {
                                partTasks.Add(MeshManager.PrepareMeshDataAsync(partId, false, ct));
                            }
                        }

                        var partMeshes = await Task.WhenAll(partTasks);
                        foreach (var partData in partMeshes) {
                            if (partData != null) {
                                _preparedMeshes.TryAdd(partData.ObjectId, partData);
                            }
                        }
                    }
                }

                lb.MeshDataReady = true;
                _uploadQueue[key] = lb;
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
