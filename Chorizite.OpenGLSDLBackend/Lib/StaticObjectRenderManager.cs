using Chorizite.Core.Lib;
using Chorizite.Core.Render;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
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
using WorldBuilder.Shared.Services;
using BoundingBox = Chorizite.Core.Lib.BoundingBox;

namespace Chorizite.OpenGLSDLBackend.Lib {
    /// <summary>
    /// Manages static object rendering (buildings, placed objects from LandBlockInfo).
    /// Extends <see cref="ObjectRenderManagerBase"/> with LandBlockInfo-based generation.
    /// Shares ObjectMeshManager with SceneryRenderManager for mesh/texture reuse.
    /// </summary>
    public class StaticObjectRenderManager : ObjectRenderManagerBase {
        private readonly IDatReaderWriter _dats;

        // Instance readiness coordination (used by SceneryRenderManager)
        private readonly ConcurrentDictionary<ushort, TaskCompletionSource> _instanceReadyTcs = new();
        private readonly object _tcsLock = new();

        // Visibility filters (Option A: stored as state, used by base PrepareRenderBatches)
        private bool _showBuildings = true;
        private bool _showStaticObjects = true;

        protected override int MaxConcurrentGenerations => Math.Max(2, System.Environment.ProcessorCount);

        public StaticObjectRenderManager(GL gl, ILogger log, LandscapeDocument landscapeDoc,
            IDatReaderWriter dats, OpenGLGraphicsDevice graphicsDevice, ObjectMeshManager meshManager, Frustum frustum)
            : base(gl, graphicsDevice, meshManager, log, landscapeDoc, frustum, true, 4096) {
            _dats = dats;
        }

        #region Public: Static Object-Specific API

        /// <summary>
        /// Sets the visibility filters for buildings and static objects.
        /// Call before <see cref="ObjectRenderManagerBase.PrepareRenderBatches"/>.
        /// </summary>
        public void SetVisibilityFilters(bool showBuildings, bool showStaticObjects) {
            if (_showBuildings == showBuildings && _showStaticObjects == showStaticObjects) return;
            _showBuildings = showBuildings;
            _showStaticObjects = showStaticObjects;

            foreach (var lb in _landblocks.Values) {
                if (lb.GpuReady) {
                    lock (lb) {
                        BuildMdiCommands(lb);
                    }
                }
            }
        }

        /// <summary>
        /// Waits until instances for a specific landblock are ready.
        /// </summary>
        public async Task WaitForInstancesAsync(ushort key, CancellationToken ct = default) {
            Task task;
            lock (_tcsLock) {
                if (_landblocks.TryGetValue(key, out var lb) && lb.InstancesReady) {
                    return;
                }
                var tcs = _instanceReadyTcs.GetOrAdd(key, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
                task = tcs.Task;
            }
            using (ct.Register(() => {
                lock (_tcsLock) {
                    if (_instanceReadyTcs.TryGetValue(key, out var tcs)) {
                        tcs.TrySetCanceled();
                    }
                }
            })) {
                await task;
            }
        }

        /// <summary>
        /// Gets the instances for a landblock.
        /// </summary>
        public List<SceneryInstance>? GetLandblockInstances(ushort key) {
            return _landblocks.TryGetValue(key, out var lb) ? lb.Instances : null;
        }

        /// <summary>
        /// Gets the pending instances for a landblock.
        /// </summary>
        public List<SceneryInstance>? GetPendingLandblockInstances(ushort key) {
            return _landblocks.TryGetValue(key, out var lb) ? lb.PendingInstances : null;
        }

        public bool IsLandblockReady(ushort key) {
            return _landblocks.TryGetValue(key, out var lb) && lb.MeshDataReady;
        }

        [Flags]
        public enum RaycastTarget {
            None = 0,
            StaticObjects = 1,
            Buildings = 2,
            All = StaticObjects | Buildings
        }

        public virtual bool Raycast(Vector3 rayOrigin, Vector3 rayDirection, RaycastTarget targets, out SceneRaycastHit hit, uint currentCellId = 0, bool isCollision = false, float maxDistance = float.MaxValue, ObjectId ignoreInstanceId = default) {
            hit = SceneRaycastHit.NoHit;

            // Early exit: Don't collide with exteriors if we are inside
            if (isCollision && currentCellId != 0) return false;

            foreach (var (key, lb) in _landblocks) {
                if (!lb.InstancesReady) continue;

                lock (lb) {
                    foreach (var instance in lb.Instances) {
                        if (ignoreInstanceId != ObjectId.Empty && instance.InstanceId == ignoreInstanceId) continue;

                        if (!isCollision) {
                            if (instance.IsBuilding && !_showBuildings) continue;
                            if (!instance.IsBuilding && !_showStaticObjects) continue;
                        }

                        if (instance.IsBuilding && !targets.HasFlag(RaycastTarget.Buildings)) continue;
                        if (!instance.IsBuilding && !targets.HasFlag(RaycastTarget.StaticObjects)) continue;

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
                                hit.Type = instance.IsBuilding ? ObjectType.Building : ObjectType.StaticObject;
                                hit.ObjectId = (uint)instance.ObjectId;
                                hit.InstanceId = instance.InstanceId;
                                hit.Position = rayOrigin + rayDirection * d;
                                hit.LocalPosition = instance.LocalPosition;
                                hit.Rotation = instance.Rotation;
                                hit.LandblockId = key;
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
                if (_frustum.TestBox(lb.BoundingBox) == FrustumTestResult.Outside) continue;

                lock (lb) {
                    foreach (var instance in lb.Instances) {
                        if (instance.IsBuilding && !settings.SelectBuildings) continue;
                        if (!instance.IsBuilding && !settings.SelectStaticObjects) continue;

                        // Skip if instance is outside frustum
                        if (!_frustum.Intersects(instance.BoundingBox)) continue;

                        var isSelected = SelectedInstance.HasValue && SelectedInstance.Value.LandblockKey == GeometryUtils.PackKey(lb.GridX, lb.GridY) && SelectedInstance.Value.InstanceId == instance.InstanceId;
                        var isHovered = HoveredInstance.HasValue && HoveredInstance.Value.LandblockKey == GeometryUtils.PackKey(lb.GridX, lb.GridY) && HoveredInstance.Value.InstanceId == instance.InstanceId;

                        Vector4 color;
                        if (isSelected) color = LandscapeColorsSettings.Instance.Selection;
                        else if (isHovered) color = LandscapeColorsSettings.Instance.Hover;
                        else if (instance.IsBuilding) color = settings.BuildingColor;
                        else color = settings.StaticObjectColor;

                        debug.DrawBox(instance.LocalBoundingBox.ToShared(), instance.Transform, color);
                    }
                }
            }
        }

        public override void RenderParticles() {
            RenderParticles(null);
        }

        public override void RenderParticles(HashSet<uint>? filter) {
            foreach (var (key, lb) in _landblocks) {
                if (!lb.InstancesReady || Math.Abs(lb.GridX - _cameraLbX) > ParticleRenderDistance || Math.Abs(lb.GridY - _cameraLbY) > ParticleRenderDistance) continue;

                foreach (var emitter in lb.ParticleEmitters) {
                    // Check if the parent instance should be visible
                    if (emitter.ParentLandblock != null && emitter.ParentInstanceId.HasValue) {
                        var instance = emitter.ParentLandblock.Instances.FirstOrDefault(i => i.InstanceId == emitter.ParentInstanceId.Value);
                        if (!instance.Equals(default(SceneryInstance))) {
                            if (instance.IsBuilding && !_showBuildings) continue;
                            if (!instance.IsBuilding && !_showStaticObjects) continue;
                        }
                    }
                    emitter.Render(GraphicsDevice.ParticleBatcher);
                }
            }
        }

        #endregion

        #region Protected: Overrides

        public override void PrepareRenderBatches(Matrix4x4 viewProjectionMatrix, Vector3 cameraPosition, HashSet<uint>? filter = null, bool isOutside = false) {
            base.PrepareRenderBatches(viewProjectionMatrix, cameraPosition, filter, isOutside);
        }

        protected override void BuildMdiCommands(ObjectLandblock lb) {
            lb.MdiCommands.Clear();
            if (lb.InstanceBufferOffset < 0) return;

            int currentOffset = 0;
            foreach (var (gfxObjId, transforms) in lb.StaticPartGroups) {
                if (_showStaticObjects) {
                    AddMdiCommandsForGroup(lb.MdiCommands, gfxObjId, transforms.Count, lb.InstanceBufferOffset, currentOffset);
                }
                currentOffset += transforms.Count;
            }
            foreach (var (gfxObjId, transforms) in lb.BuildingPartGroups) {
                if (_showBuildings) {
                    AddMdiCommandsForGroup(lb.MdiCommands, gfxObjId, transforms.Count, lb.InstanceBufferOffset, currentOffset);
                }
                currentOffset += transforms.Count;
            }
        }

        protected override IEnumerable<KeyValuePair<ulong, List<InstanceData>>> GetFastPathGroups(ObjectLandblock lb) {
            if (_showBuildings) {
                foreach (var kvp in lb.BuildingPartGroups) {
                    yield return kvp;
                }
            }
            if (_showStaticObjects) {
                foreach (var kvp in lb.StaticPartGroups) {
                    yield return kvp;
                }
            }
        }

        protected override bool ShouldIncludeInstance(SceneryInstance instance) {
            if (instance.IsBuilding && !_showBuildings) return false;
            if (!instance.IsBuilding && !_showStaticObjects) return false;
            return true;
        }

        protected override void PopulatePartGroups(ObjectLandblock lb, List<SceneryInstance> instances) {
            lb.StaticPartGroups.Clear();
            lb.BuildingPartGroups.Clear();
            foreach (var instance in instances) {
                var targetGroup = instance.IsBuilding ? lb.BuildingPartGroups : lb.StaticPartGroups;
                var cellId = instance.CurrentPreviewCellId != 0 ? instance.CurrentPreviewCellId : instance.InstanceId.DataId;
                PopulateRecursive(targetGroup, instance.ObjectId, instance.IsSetup, instance.Transform, cellId);
            }
        }

        protected override void OnUnloadResources(ObjectLandblock lb, ushort key) {
            lock (_tcsLock) {
                if (_instanceReadyTcs.TryRemove(key, out var tcs)) {
                    tcs.TrySetCanceled();
                }
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
                if (_instanceReadyTcs.TryRemove(key, out var tcs)) {
                    tcs.TrySetCanceled();
                }
            }
        }

        protected override float GetPriority(ObjectLandblock lb, Vector2 camDir2D, int cameraLbX, int cameraLbY) {
            var priority = base.GetPriority(lb, camDir2D, cameraLbX, cameraLbY);

            // Prioritize landblocks with buildings
            var lbId = (ushort)((uint)lb.GridX << 8 | (uint)lb.GridY);
            var mergedLb = LandscapeDoc.GetMergedLandblock(lbId);
            if (mergedLb.Buildings.Count > 0) {
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
                var lbId = (ushort)(lbGlobalX << 8 | lbGlobalY);
                var lbFileId = ((uint)lbId << 16) | 0xFFFE;

                var staticObjects = new List<SceneryInstance>();
                var lbSizeUnits = regionInfo.LandblockSizeInUnits; // 192

                var mergedLb = await LandscapeDoc.GetMergedLandblockAsync(lbId);

                // Placed objects
                foreach (var obj in mergedLb.StaticObjects.Values) {
                    if (obj.ModelId == 0) continue;

                    var isSetup = (obj.ModelId >> 24) == 0x02;
                    var localPos = obj.Position;
                    var worldPos = new Vector3(
                        new Vector2(lbGlobalX * lbSizeUnits + obj.Position.X, lbGlobalY * lbSizeUnits + obj.Position.Y) + regionInfo.MapOffset,
                        obj.Position.Z
                    );

                    var rotation = obj.Rotation;

                    var transform = Matrix4x4.CreateFromQuaternion(rotation)
                        * Matrix4x4.CreateTranslation(worldPos);
                    var bounds = MeshManager.GetBounds(obj.ModelId, isSetup);
                    var localBbox = bounds.HasValue ? new BoundingBox(bounds.Value.Min, bounds.Value.Max) : default;
                    var bbox = localBbox.Transform(transform);

                    staticObjects.Add(new SceneryInstance {
                        ObjectId = obj.ModelId,
                        InstanceId = obj.InstanceId,
                        IsSetup = isSetup,
                        IsBuilding = false,
                        WorldPosition = worldPos,
                        LocalPosition = localPos,
                        Rotation = rotation,
                        Scale = Vector3.One,
                        Transform = transform,
                        LocalBoundingBox = localBbox,
                        BoundingBox = bbox
                    });
                }

                // Buildings
                foreach (var building in mergedLb.Buildings.Values) {
                    if (building.ModelId == 0) continue;

                    var isSetup = (building.ModelId >> 24) == 0x02;
                    var localPos = building.Position;
                    var worldPos = new Vector3(
                        new Vector2(lbGlobalX * lbSizeUnits + building.Position.X, lbGlobalY * lbSizeUnits + building.Position.Y) + regionInfo.MapOffset,
                        building.Position.Z
                    );

                    var rotation = building.Rotation;

                    var transform = Matrix4x4.CreateFromQuaternion(rotation)
                        * Matrix4x4.CreateTranslation(worldPos);

                    var bounds = MeshManager.GetBounds(building.ModelId, isSetup);
                    var localBbox = bounds.HasValue ? new BoundingBox(bounds.Value.Min, bounds.Value.Max) : default;
                    var bbox = localBbox.Transform(transform);

                    staticObjects.Add(new SceneryInstance {
                        ObjectId = building.ModelId,
                        InstanceId = building.InstanceId,
                        IsSetup = isSetup,
                        IsBuilding = true,
                        WorldPosition = worldPos,
                        LocalPosition = localPos,
                        Rotation = rotation,
                        Scale = Vector3.One,
                        Transform = transform,
                        LocalBoundingBox = localBbox,
                        BoundingBox = bbox
                    });
                }

                lb.PendingInstances = staticObjects;

                lock (_tcsLock) {
                    lb.InstancesReady = true;
                    if (_instanceReadyTcs.TryGetValue(key, out var tcs)) {
                        tcs.TrySetResult();
                    }
                }

                if (staticObjects.Count > 0) {
                    Log.LogTrace("Generated {Count} static objects for landblock ({X},{Y})", staticObjects.Count, lb.GridX, lb.GridY);
                }

                // Prepare mesh data for unique objects on background thread
                await PrepareMeshesForInstances(staticObjects, ct);

                lb.MeshDataReady = true;
                _uploadQueue[key] = lb;
            }
            catch (OperationCanceledException) {
                // Ignore cancellations
            }
            catch (Exception ex) {
                Log.LogError(ex, "Error generating static objects for landblock ({X},{Y})", lb.GridX, lb.GridY);
            }
        }

        #endregion

        public override void Dispose() {
            base.Dispose();
        }
    }
}
