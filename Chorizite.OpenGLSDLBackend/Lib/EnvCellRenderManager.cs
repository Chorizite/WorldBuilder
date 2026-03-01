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

        // Grouped instances by cellId for efficient portal-based rendering
        private readonly Dictionary<uint, Dictionary<ulong, List<InstanceData>>> _batchedByCell = new();

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

            // Is the current XY position outside the map?
            if (lbX < 0 || lbY < 0 || lbX >= LandscapeDoc.Region.MapWidthInLandblocks || lbY >= LandscapeDoc.Region.MapHeightInLandblocks) return 0;

            // Only check landblocks in a 3x3 neighborhood of the position.
            // Most EnvCells are within their originating landblock or its immediate neighbors.
            for (int x = lbX - 1; x <= lbX + 1; x++) {
                for (int y = lbY - 1; y <= lbY + 1; y++) {
                    var key = GeometryUtils.PackKey(x, y);
                    if (!_landblocks.TryGetValue(key, out var lb) || !lb.InstancesReady || !lb.MeshDataReady) continue;

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

        public static ulong GetEnvCellGeomId(ushort environmentId, ushort cellStructure, List<ushort> surfaces) {
            var hash = 17L;
            hash = hash * 31 + environmentId;
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

        public bool Raycast(Vector3 rayOrigin, Vector3 rayDirection, bool includeCells, bool includeStaticObjects, out SceneRaycastHit hit, uint currentCellId = 0, bool isCollision = false, float maxDistance = float.MaxValue) {
            hit = SceneRaycastHit.NoHit;

            // Early exit: Don't collide with interiors if we are outside
            if (isCollision && currentCellId == 0) return false;

            ushort? targetLbKey = currentCellId != 0 ? (ushort)(currentCellId >> 16) : null;

            foreach (var (key, lb) in _landblocks) {
                if (!lb.InstancesReady) continue;

                // If we know which landblock we are in, only check that one
                if (targetLbKey.HasValue && key != targetLbKey.Value) continue;

                lock (lb) {
                    foreach (var instance in lb.Instances) {
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
                if (!lb.InstancesReady || !IsWithinRenderDistance(lb) || lb.Instances.Count == 0) continue;
                if (_frustum.TestBox(lb.TotalEnvCellBounds) == FrustumTestResult.Outside) continue;

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

        public override void PrepareRenderBatches(Matrix4x4 viewProjectionMatrix, Vector3 cameraPosition, HashSet<uint>? filter = null, bool isOutside = false) {
            if (!_initialized || cameraPosition.Z > 4000) return;

            // Clear previous frame data
            _visibleGroups.Clear();
            _visibleGfxObjIds.Clear();
            _poolIndex = 0;
            _batchedByCell.Clear();

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

            Parallel.ForEach(landblocks, lb => {
                var testResult = _frustum.TestBox(lb.TotalEnvCellBounds);
                if (testResult == FrustumTestResult.Outside) return;

                var seenOutsideCells = lb.SeenOutsideCells;
                var lbBatchedByCell = threadLocalBatchedByCell.Value!;
                var lbGlobalGroups = threadLocalGlobalGroups.Value!;

                // Fast path: Landblock is fully inside frustum
                if (testResult == FrustumTestResult.Inside) {
                    foreach (var (gfxObjId, instances) in lb.BuildingPartGroups) {
                        foreach (var instanceData in instances) {
                            if (filter != null && !filter.Contains(instanceData.CellId)) continue;
                            if (isOutside && filter == null && seenOutsideCells != null && !seenOutsideCells.Contains(instanceData.CellId)) continue;

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
                    if (isOutside && filter == null && seenOutsideCells != null && !seenOutsideCells.Contains(cellId)) continue;

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
            });

            // Merge results from all threads
            foreach (var localBatchedByCell in threadLocalBatchedByCell.Values) {
                foreach (var cellKvp in localBatchedByCell) {
                    if (!_batchedByCell.TryGetValue(cellKvp.Key, out var gfxDict)) {
                        gfxDict = new Dictionary<ulong, List<InstanceData>>();
                        _batchedByCell[cellKvp.Key] = gfxDict;
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
                    if (!_visibleGroups.TryGetValue(kvp.Key, out var list)) {
                        list = GetPooledList();
                        _visibleGroups[kvp.Key] = list;
                        _visibleGfxObjIds.Add(kvp.Key);
                    }
                    list.AddRange(kvp.Value);
                }
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

        public override void Render(int renderPass) {
            Render(renderPass, null);
        }

        public unsafe void Render(int renderPass, HashSet<uint>? filter) {
            if (!_initialized || _shader is null || (_shader is GLSLShader glsl && glsl.Program == 0) || (_cameraPosition.Z > 4000 && renderPass != 2)) return;

            CurrentVAO = 0;
            CurrentIBO = 0;
            CurrentAtlas = 0;
            CurrentCullMode = null;

            _shader.SetUniform("uRenderPass", renderPass);
            _shader.SetUniform("uFilterByCell", 0);

            var allInstances = new List<InstanceData>();
            var drawCalls = new List<(ObjectRenderData renderData, int count, int offset)>();

            if (filter == null) {
                // Optimized path: Use global groups batched across all cells
                foreach (var gfxObjId in _visibleGfxObjIds) {
                    if (_visibleGroups.TryGetValue(gfxObjId, out var transforms)) {
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
                foreach (var cellId in filter) {
                    if (_batchedByCell.TryGetValue(cellId, out var gfxDict)) {
                        foreach (var (gfxObjId, transforms) in gfxDict) {
                            if (transforms.Count > 0) {
                                if (!filteredGroups.TryGetValue(gfxObjId, out var list)) {
                                    list = transforms; // Optimization: just use the first list
                                    filteredGroups[gfxObjId] = list;
                                }
                                else {
                                    if (list == transforms) continue;

                                    // If we already have a list for this GfxObjId from another cell, we need to merge.
                                    if (list.Count > 0 && !IsPooled(list)) {
                                        var newList = GetPooledList();
                                        newList.AddRange(list);
                                        list = newList;
                                        filteredGroups[gfxObjId] = list;
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
                // Upload all instance data in one go (with orphaning)
                GraphicsDevice.EnsureInstanceBufferCapacity(allInstances.Count, sizeof(InstanceData), true);
                Gl.BindBuffer(GLEnum.ArrayBuffer, GraphicsDevice.InstanceVBO);
                var span = CollectionsMarshal.AsSpan(allInstances);
                fixed (InstanceData* ptr = span) {
                    Gl.BufferSubData(GLEnum.ArrayBuffer, 0, (nuint)(allInstances.Count * sizeof(InstanceData)), ptr);
                }

                // Issue draw calls
                foreach (var call in drawCalls) {
                    RenderObjectBatches(_shader!, call.renderData, call.count, call.offset);
                }
            }

            // Draw highlighted / selected objects on top
            if (RenderHighlightsWhenEmpty || _batchedByCell.Count > 0) {
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

        private bool IsPooled(List<InstanceData> list) {
            // Simple check to see if it's one of our pooled lists
            return _listPool.Contains(list);
        }

        protected override void PopulatePartGroups(ObjectLandblock lb, List<SceneryInstance> instances) {
            lb.BuildingPartGroups.Clear(); // Using BuildingPartGroups for EnvCell parts
            foreach (var instance in instances) {
                var targetGroup = lb.BuildingPartGroups;
                var cellId = InstanceIdConstants.GetRawId(instance.InstanceId);
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
                var envCellBounds = new Dictionary<uint, BoundingBox>();
                var seenOutsideCells = new HashSet<uint>();
                var cellGeomIdToEnvCell = new Dictionary<ulong, EnvCell>();

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

                        if (envCell.Flags.HasFlag(EnvCellFlags.SeenOutside)) {
                            seenOutsideCells.Add(cellId);
                        }

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
                                // Use deduplicated ID for cell geometry
                                var cellGeomId = GetEnvCellGeomId(envCell.EnvironmentId, envCell.CellStructure, envCell.Surfaces);
                                cellGeomIdToEnvCell[cellGeomId] = envCell;
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

                                if (envCellBounds.TryGetValue(cellId, out var currentBox)) {
                                    envCellBounds[cellId] = currentBox.Union(bbox);
                                }
                                else {
                                    envCellBounds[cellId] = bbox;
                                }
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

                    if (cellGeomIdToEnvCell.TryGetValue(objectId, out var cell)) {
                        preparationTasks.Add(MeshManager.PrepareEnvCellGeomMeshDataAsync(objectId, cell, ct));
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
