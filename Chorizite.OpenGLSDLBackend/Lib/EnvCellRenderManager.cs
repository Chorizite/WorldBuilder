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

        // Optional cell filter for per-building stencil rendering.
        // When non-null, only instances belonging to these cell IDs are included.
        private HashSet<uint>? _cellFilter = null;

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

        /// <summary>
        /// Sets a cell filter so that only instances belonging to the specified cell IDs
        /// are included in the next PrepareRenderBatches/Render cycle.
        /// Used for per-building stencil rendering.
        /// </summary>
        public void SetCellFilter(HashSet<uint> cellIds) {
            _cellFilter = cellIds;
        }

        /// <summary>
        /// Clears the cell filter, allowing all cells to be rendered.
        /// </summary>
        public void ClearCellFilter() {
            _cellFilter = null;
        }

        public uint GetEnvCellAt(Vector3 pos, bool onlyEntryCells = false) {
            if (LandscapeDoc.Region == null) return 0;

            var lbSize = LandscapeDoc.Region.LandblockSizeInUnits;
            var mapPos = new Vector2(pos.X, pos.Y) - LandscapeDoc.Region.MapOffset;
            int lbX = (int)Math.Floor(mapPos.X / lbSize);
            int lbY = (int)Math.Floor(mapPos.Y / lbSize);

            var key = GeometryUtils.PackKey(lbX, lbY);

            if (_landblocks.TryGetValue(key, out var lb)) {
                if (!lb.InstancesReady) return 0;
                lock (lb) {
                    foreach (var instance in lb.Instances) {
                        var type = InstanceIdConstants.GetType(instance.InstanceId);
                        if (type != InspectorSelectionType.EnvCell) continue;
                        if (onlyEntryCells && !instance.IsEntryCell) continue;

                        if (instance.BoundingBox.Contains(pos)) {
                            return InstanceIdConstants.GetRawId(instance.InstanceId);
                        }
                    }
                }
            }
            return 0;
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

        protected override IEnumerable<KeyValuePair<ulong, List<Matrix4x4>>> GetFastPathGroups(ObjectLandblock lb) {
            if (!_showEnvCells) yield break;

            if (_cellFilter == null) {
                // No filter: return all groups (original behavior)
                foreach (var kvp in lb.BuildingPartGroups) {
                    yield return kvp;
                }
            }
            else {
                // With filter: iterate instances and only include matching cells.
                // We can't use BuildingPartGroups because they don't store per-instance cell IDs.
                foreach (var instance in lb.Instances) {
                    var cellId = InstanceIdConstants.GetRawId(instance.InstanceId);
                    if (!_cellFilter.Contains(cellId)) continue;

                    if (instance.IsSetup) {
                        var renderData = MeshManager.TryGetRenderData(instance.ObjectId);
                        if (renderData is { IsSetup: true }) {
                            foreach (var (partId, partTransform) in renderData.SetupParts) {
                                yield return new KeyValuePair<ulong, List<Matrix4x4>>(
                                    partId, new List<Matrix4x4> { partTransform * instance.Transform });
                            }
                        }
                    }
                    else {
                        yield return new KeyValuePair<ulong, List<Matrix4x4>>(
                            instance.ObjectId, new List<Matrix4x4> { instance.Transform });
                    }
                }
            }
        }

        protected override bool ShouldIncludeInstance(SceneryInstance instance) {
            if (!_showEnvCells) return false;
            if (_cellFilter == null) return true;

            // Extract the cell ID from the instance ID
            var cellId = InstanceIdConstants.GetRawId(instance.InstanceId);
            return _cellFilter.Contains(cellId);
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
                        // Check if this cell should be rendered
                        if (envCell.Flags.HasFlag(EnvCellFlags.SeenOutside)) {
                            numVisibleCells++;

                            // Calculate world position
                            var localPos = new Vector3(
                                new Vector2(lbGlobalX * lbSizeUnits + (float)envCell.Position.Origin[0], lbGlobalY * lbSizeUnits + (float)envCell.Position.Origin[1]) + regionInfo.MapOffset,
                                envCell.Position.Origin[2]
                            );

                            var rotation = new Quaternion(
                                envCell.Position.Orientation[0], // X
                                envCell.Position.Orientation[1], // Y
                                envCell.Position.Orientation[2], // Z
                                envCell.Position.Orientation[3]  // W
                            );

                            var transform = Matrix4x4.CreateFromQuaternion(rotation)
                                * Matrix4x4.CreateTranslation(localPos);

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
                                        WorldPosition = localPos,
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

                                    var stabWorldPos = new Vector3(
                                        new Vector2(lbGlobalX * lbSizeUnits + (float)stab.Frame.Origin[0], lbGlobalY * lbSizeUnits + (float)stab.Frame.Origin[1]) + regionInfo.MapOffset,
                                        stab.Frame.Origin[2]
                                    );

                                    var stabWorldRot = new Quaternion(stab.Frame.Orientation[0], stab.Frame.Orientation[1], stab.Frame.Orientation[2], stab.Frame.Orientation[3]);
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
                                        Rotation = stabWorldRot,
                                        Scale = Vector3.One,
                                        Transform = stabWorldTransform,
                                        LocalBoundingBox = localBbox,
                                        BoundingBox = bbox
                                    });
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
