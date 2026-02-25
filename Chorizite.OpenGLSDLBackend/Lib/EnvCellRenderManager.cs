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

        #endregion

        #region Protected: Overrides

        protected override IEnumerable<KeyValuePair<uint, List<Matrix4x4>>> GetFastPathGroups(ObjectLandblock lb) {
            if (_showEnvCells) {
                foreach (var kvp in lb.BuildingPartGroups) { // Recycle BuildingPartGroups for EnvCells
                    yield return kvp;
                }
            }
        }

        protected override bool ShouldIncludeInstance(SceneryInstance instance) {
            return _showEnvCells;
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
                var cellsToProcess = new Queue<uint>();

                if (_dats.CellRegions.TryGetValue(regionInfo.Region.RegionNumber, out var cellDb)) {
                    if (cellDb.TryGet<LandBlockInfo>(lbId, out var lbi)) {
                        foreach (var building in mergedLb.Buildings) {
                            // Only base buildings have portal info for now
                            var isBase = (building.InstanceId & 0x80000000) != 0;
                            if (isBase) {
                                int index = (int)(building.InstanceId & 0x3FFFFFFF);
                                if (index < lbi.Buildings.Count) {
                                    var bInfo = lbi.Buildings[index];
                                    // Start discovery from building portals
                                    foreach (var portal in bInfo.Portals) {
                                        if (portal.OtherCellId != 0xFFFF) {
                                            var cellId = (lbId & 0xFFFF0000) | portal.OtherCellId;
                                            if (discoveredCellIds.Add(cellId)) {
                                                cellsToProcess.Enqueue(cellId);
                                            }
                                        }
                                    }
                                }
                            }
                            // TODO: Support portals on custom buildings when we have that data
                        }
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
                                (float)envCell.Position.Origin[2]
                            );

                            var rotation = new Quaternion(
                                (float)envCell.Position.Orientation[0], // X
                                (float)envCell.Position.Orientation[1], // Y
                                (float)envCell.Position.Orientation[2], // Z
                                (float)envCell.Position.Orientation[3]  // W
                            );

                            var transform = Matrix4x4.CreateFromQuaternion(rotation)
                                * Matrix4x4.CreateTranslation(localPos);

                            var isSetup = true; // Force isSetup true for EnvCells
                            var bounds = MeshManager.GetBounds(envCell.Id, isSetup);
                            var localBbox = bounds.HasValue ? new BoundingBox(bounds.Value.Min, bounds.Value.Max) : default;
                            var bbox = localBbox.Transform(transform);

                            instances.Add(new SceneryInstance {
                                ObjectId = envCell.Id,
                                InstanceId = 0,
                                IsSetup = isSetup,
                                IsBuilding = false,
                                WorldPosition = localPos,
                                Rotation = rotation,
                                Scale = Vector3.One,
                                Transform = transform,
                                LocalBoundingBox = localBbox,
                                BoundingBox = bbox
                            });
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

                if (instances.Count > 0) {
                    Log.LogTrace("Generated {Count} EnvCells ({Visited} visited) for landblock ({X},{Y})", instances.Count, numVisibleCells, lb.GridX, lb.GridY);
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

        public override void Dispose() {
            base.Dispose();
        }
    }
}
