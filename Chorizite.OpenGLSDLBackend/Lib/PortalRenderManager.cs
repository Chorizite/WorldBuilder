using Chorizite.Core.Lib;
using Chorizite.Core.Render;
using Chorizite.Core.Render.Vertex;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Numerics;
using WorldBuilder.Shared.Services;
using VertexAttribType = Chorizite.Core.Render.Enums.VertexAttribType;
using BufferUsage = Chorizite.Core.Render.Enums.BufferUsage;
using PrimitiveType = Silk.NET.OpenGL.PrimitiveType;
using BoundingBox = WorldBuilder.Shared.Numerics.BoundingBox;
using ICamera = WorldBuilder.Shared.Models.ICamera;

namespace Chorizite.OpenGLSDLBackend.Lib {
    /// <summary>
    /// Manages portal rendering.
    /// Portals are semi-transparent magenta polygons that connect cells to the outside world.
    /// Also manages GPU-uploaded portal polygon meshes for stencil-based interior rendering.
    /// </summary>
    public class PortalRenderManager : IDisposable {
        private readonly GL _gl;
        private readonly ILogger _log;
        private readonly LandscapeDocument _landscapeDoc;
        private readonly IDatReaderWriter _dats;
        private readonly IPortalService _portalService;
        private readonly OpenGLGraphicsDevice _graphicsDevice;

        // Per-landblock portal data
        private readonly ConcurrentDictionary<ushort, PortalLandblock> _landblocks = new();
        private readonly ConcurrentDictionary<ushort, PortalLandblock> _pendingGeneration = new();
        private readonly ConcurrentQueue<PortalLandblock> _uploadQueue = new();
        private int _activeGenerations = 0;

        public bool ShowPortals { get; set; } = true;
        public int RenderDistance { get; set; } = 12;

        public (uint CellId, ulong PortalIndex)? HoveredPortal { get; set; }
        public (uint CellId, ulong PortalIndex)? SelectedPortal { get; set; }

        private Vector3 _cameraPosition;
        private int _cameraLbX;
        private int _cameraLbY;
        private float _lbSizeInUnits;
        private readonly Frustum _frustum;

        // Stencil rendering
        private IShader? _stencilShader;

        public PortalRenderManager(GL gl, ILogger log, LandscapeDocument landscapeDoc,
            IDatReaderWriter dats, IPortalService portalService, OpenGLGraphicsDevice graphicsDevice, Frustum frustum) {
            _gl = gl;
            _log = log;
            _landscapeDoc = landscapeDoc;
            _dats = dats;
            _portalService = portalService;
            _graphicsDevice = graphicsDevice;
            _frustum = frustum;

            _landscapeDoc.LandblockChanged += OnLandblockChanged;
        }

        /// <summary>
        /// Initializes the stencil shader for portal stencil rendering.
        /// Must be called after the GL context is ready.
        /// </summary>
        public void InitializeStencilShader(IShader stencilShader) {
            _stencilShader = stencilShader;
        }

        public void OnLandblockChanged(object? sender, LandblockChangedEventArgs e) {
            if (e.AffectedLandblocks == null) {
                foreach (var lb in _landblocks.Values) {
                    lb.Ready = false;
                    var key = GeometryUtils.PackKey(lb.GridX, lb.GridY);
                    _pendingGeneration[key] = lb;
                }
            }
            else {
                foreach (var (lbX, lbY) in e.AffectedLandblocks) {
                    var key = GeometryUtils.PackKey(lbX, lbY);
                    if (_landblocks.TryGetValue(key, out var lb)) {
                        lb.Ready = false;
                        _pendingGeneration[key] = lb;
                    }
                }
            }
        }

        public void Update(float deltaTime, ICamera camera) {
            if (_landscapeDoc.Region == null) return;

            var region = _landscapeDoc.Region;
            var lbSize = region.CellSizeInUnits * region.LandblockCellLength;
            _lbSizeInUnits = lbSize;

            _cameraPosition = camera.Position;
            var pos = new Vector2(_cameraPosition.X, _cameraPosition.Y) - region.MapOffset;
            _cameraLbX = (int)Math.Floor(pos.X / lbSize);
            _cameraLbY = (int)Math.Floor(pos.Y / lbSize);

            // Queue landblocks within render distance
            for (int x = _cameraLbX - RenderDistance; x <= _cameraLbX + RenderDistance; x++) {
                for (int y = _cameraLbY - RenderDistance; y <= _cameraLbY + RenderDistance; y++) {
                    if (x < 0 || y < 0 || x >= region.MapWidthInLandblocks || y >= region.MapHeightInLandblocks)
                        continue;

                    var key = GeometryUtils.PackKey(x, y);
                    if (!_landblocks.ContainsKey(key)) {
                        var lb = new PortalLandblock {
                            GridX = x,
                            GridY = y
                        };
                        if (_landblocks.TryAdd(key, lb)) {
                            _pendingGeneration[key] = lb;
                        }
                    }
                }
            }

            // Unload out-of-range landblocks
            var keysToRemove = _landblocks.Keys.Where(key => {
                var x = key >> 8;
                var y = key & 0xFF;
                return Math.Abs(x - _cameraLbX) > RenderDistance || Math.Abs(y - _cameraLbY) > RenderDistance;
            }).ToList();

            foreach (var key in keysToRemove) {
                if (_landblocks.TryRemove(key, out var lb)) {
                    UnloadLandblock(lb);
                }
                _pendingGeneration.TryRemove(key, out _);
            }

            // Start generation tasks
            while (_activeGenerations < 4 && _pendingGeneration.TryRemove(_pendingGeneration.Keys.FirstOrDefault(), out var lbToGenerate)) {
                Interlocked.Increment(ref _activeGenerations);
                Task.Run(async () => {
                    try {
                        await GeneratePortalsForLandblock(lbToGenerate);
                    }
                    finally {
                        Interlocked.Decrement(ref _activeGenerations);
                    }
                });
            }
        }

        public float ProcessUploads(float timeBudgetMs) {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalMilliseconds < timeBudgetMs && _uploadQueue.TryDequeue(out var lb)) {
                UploadLandblock(lb);
            }
            return (float)sw.Elapsed.TotalMilliseconds;
        }

        public void SubmitDebugShapes(DebugRenderer? debug) {
            if (debug == null || !ShowPortals || _landscapeDoc.Region == null) return;

            var portalColor = LandscapeColorsSettings.Instance.Portal;
            var hoverColor = LandscapeColorsSettings.Instance.Hover;
            var selectionColor = LandscapeColorsSettings.Instance.Selection;

            foreach (var lb in _landblocks.Values) {
                if (!lb.Ready) continue;

                foreach (var portal in lb.Portals) {
                    var color = portalColor;
                    if (HoveredPortal.HasValue && HoveredPortal.Value.CellId == portal.CellId && InstanceIdConstants.GetRawId(HoveredPortal.Value.PortalIndex) == portal.PortalIndex) {
                        color = hoverColor;
                    }
                    if (SelectedPortal.HasValue && SelectedPortal.Value.CellId == portal.CellId && InstanceIdConstants.GetRawId(SelectedPortal.Value.PortalIndex) == portal.PortalIndex) {
                        color = selectionColor;
                    }

                    for (int i = 0; i < portal.Vertices.Length; i++) {
                        debug.DrawLine(portal.Vertices[i], portal.Vertices[(i + 1) % portal.Vertices.Length], color, 5.0f);
                    }
                }
            }
        }

        public bool Raycast(Vector3 rayOrigin, Vector3 rayDirection, out SceneRaycastHit hit, float maxDistance = float.MaxValue, bool ignoreVisibility = false) {
            hit = SceneRaycastHit.NoHit;
            if ((!ShowPortals && !ignoreVisibility) || _landscapeDoc.Region == null) return false;

            float closestDistance = float.MaxValue;
            PortalData? closestPortal = null;
            uint closestLandblockId = 0;

            foreach (var lb in _landblocks.Values) {
                if (!lb.Ready) continue;

                if (!RaycastingUtils.RayIntersectsBox(rayOrigin, rayDirection, lb.BoundingBox.Min, lb.BoundingBox.Max, out float lbDist) || lbDist > maxDistance) {
                    continue;
                }

                var lbGlobalX = (uint)lb.GridX;
                var lbGlobalY = (uint)lb.GridY;
                var lbId = (uint)((lbGlobalX << 24) | (lbGlobalY << 16));

                foreach (var portal in lb.Portals) {
                    if (!RaycastingUtils.RayIntersectsBox(rayOrigin, rayDirection, portal.BoundingBox.Min, portal.BoundingBox.Max, out float pDist) || pDist > maxDistance) {
                        continue;
                    }

                    if (RaycastingUtils.RayIntersectsPolygon(rayOrigin, rayDirection, portal.Vertices, out float distance)) {
                        if (distance < closestDistance && distance <= maxDistance) {
                            closestDistance = distance;
                            closestPortal = portal;
                            closestLandblockId = lbId;
                        }
                    }
                }
            }

            if (closestPortal != null) {
                var pos = rayOrigin + rayDirection * closestDistance;
                hit = new SceneRaycastHit {
                    Hit = true,
                    Type = InspectorSelectionType.Portal,
                    Distance = closestDistance,
                    Position = pos,
                    LocalPosition = pos,
                    Rotation = Quaternion.Identity,
                    LandblockId = closestLandblockId,
                    ObjectId = closestPortal.CellId,
                    InstanceId = InstanceIdConstants.Encode((uint)closestPortal.PortalIndex, InspectorSelectionType.Portal)
                };
                return true;
            }

            return false;
        }

        #region Stencil Rendering

        /// <summary>
        /// Returns an enumerable of visible building portal groups across all loaded landblocks.
        /// Each entry provides the GPU data needed to render the stencil mask and the set of
        /// EnvCell IDs to render through it.
        /// </summary>
        internal void GetVisibleBuildingPortals(List<(ushort LbKey, BuildingPortalGPU Building)> results) {
            results.Clear();
            foreach (var (key, lb) in _landblocks) {
                if (!lb.Ready || lb.BuildingPortals.Count == 0 || !IsWithinRenderDistance(lb)) continue;

                // Use the precise bounding box of the landblock's portals for frustum testing
                if (_frustum.TestBox(new Chorizite.Core.Lib.BoundingBox(lb.BoundingBox.Min, lb.BoundingBox.Max), ignoreNearPlane: true) == FrustumTestResult.Outside) continue;

                foreach (var building in lb.BuildingPortals) {
                    if (_frustum.TestBox(new Chorizite.Core.Lib.BoundingBox(building.BoundingBox.Min, building.BoundingBox.Max), ignoreNearPlane: true) == FrustumTestResult.Outside) continue;
                    results.Add((key, building));
                }
            }
        }

        /// <summary>
        /// Finds all building portal groups that contain the specified EnvCell ID.
        /// Does NOT perform frustum culling, as this is used to identify portals of the building the camera is in.
        /// </summary>
        internal void GetBuildingPortalsByCellId(uint cellId, List<(ushort LbKey, BuildingPortalGPU Building)> results) {
            results.Clear();
            foreach (var (key, lb) in _landblocks) {
                if (!lb.Ready) continue;
                foreach (var building in lb.BuildingPortals) {
                    if (building.EnvCellIds.Contains(cellId)) {
                        results.Add((key, building));
                    }
                }
            }
        }

        /// <summary>
        /// Returns an enumerable of visible building portal groups across all loaded landblocks.
        /// Each entry provides the GPU data needed to render the stencil mask and the set of
        /// EnvCell IDs to render through it.
        /// </summary>
        internal IEnumerable<(ushort LbKey, BuildingPortalGPU Building)> GetVisibleBuildingPortals() {
            foreach (var (key, lb) in _landblocks) {
                if (!lb.Ready || lb.BuildingPortals.Count == 0 || !IsWithinRenderDistance(lb)) continue;

                // Use the precise bounding box of the landblock's portals for frustum testing
                if (_frustum.TestBox(new Chorizite.Core.Lib.BoundingBox(lb.BoundingBox.Min, lb.BoundingBox.Max), ignoreNearPlane: true) == FrustumTestResult.Outside) continue;

                foreach (var building in lb.BuildingPortals) {
                    if (_frustum.TestBox(new Chorizite.Core.Lib.BoundingBox(building.BoundingBox.Min, building.BoundingBox.Max), ignoreNearPlane: true) == FrustumTestResult.Outside) continue;
                    yield return (key, building);
                }
            }
        }

        /// <summary>
        /// Finds all building portal groups that contain the specified EnvCell ID.
        /// Does NOT perform frustum culling, as this is used to identify portals of the building the camera is in.
        /// </summary>
        internal IEnumerable<(ushort LbKey, BuildingPortalGPU Building)> GetBuildingPortalsByCellId(uint cellId) {
            foreach (var (key, lb) in _landblocks) {
                if (!lb.Ready) continue;
                foreach (var building in lb.BuildingPortals) {
                    if (building.EnvCellIds.Contains(cellId)) {
                        yield return (key, building);
                    }
                }
            }
        }

        private bool IsWithinRenderDistance(PortalLandblock lb) {
            return Math.Abs(lb.GridX - _cameraLbX) <= RenderDistance && Math.Abs(lb.GridY - _cameraLbY) <= RenderDistance;
        }

        private FrustumTestResult GetLandblockFrustumResult(int gridX, int gridY) {
            var min = new Vector3(gridX * 192f + _landscapeDoc.Region!.MapOffset.X, gridY * 192f + _landscapeDoc.Region!.MapOffset.Y, -1000f);
            var max = new Vector3(min.X + 192f, min.Y + 192f, 4000f);
            return _frustum.TestBox(new Chorizite.Core.Lib.BoundingBox(min, max));
        }

        /// <summary>
        /// Renders the stencil mask for a single building's portal polygons.
        /// Caller is responsible for setting up stencil state before calling.
        /// </summary>
        internal unsafe void RenderBuildingStencilMask(BuildingPortalGPU building, Matrix4x4 viewProjection, bool writeFarDepth = false) {
            if (_stencilShader == null || building.VAO == 0) return;

            _gl.Enable(EnableCap.DepthClamp);

            _stencilShader.Bind();
            _stencilShader.SetUniform("uViewProjection", viewProjection);
            _stencilShader.SetUniform("uWriteFarDepth", writeFarDepth ? 1 : 0);

            _gl.BindVertexArray(building.VAO);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)building.VertexCount);
            _gl.BindVertexArray(0);

            _gl.Disable(EnableCap.DepthClamp);
        }

        #endregion

        private async Task GeneratePortalsForLandblock(PortalLandblock lb) {
            try {
                var lbGlobalX = (uint)lb.GridX;
                var lbGlobalY = (uint)lb.GridY;
                var lbId = (uint)((lbGlobalX << 24) | (lbGlobalY << 16));

                var lbOrigin = new Vector3(
                    lbGlobalX * 192f + _landscapeDoc.Region!.MapOffset.X,
                    lbGlobalY * 192f + _landscapeDoc.Region!.MapOffset.Y,
                    0f
                );

                // Generate debug portal data (existing functionality)
                var portals = _portalService.GetPortalsForLandblock(_landscapeDoc.RegionId, lbId).ToList();
                var lbMin = new Vector3(float.MaxValue);
                var lbMax = new Vector3(float.MinValue);

                foreach (var portal in portals) {
                    // Adjust vertices to include region offset
                    for (int i = 0; i < portal.Vertices.Length; i++) {
                        portal.Vertices[i] += lbOrigin;
                    }
                    portal.BoundingBox = new BoundingBox(portal.BoundingBox.Min + lbOrigin, portal.BoundingBox.Max + lbOrigin);

                    lbMin = Vector3.Min(lbMin, portal.BoundingBox.Min);
                    lbMax = Vector3.Max(lbMax, portal.BoundingBox.Max);
                }

                // Generate per-building portal mesh data for stencil rendering
                var buildingGroups = _portalService.GetPortalsByBuilding(_landscapeDoc.RegionId, lbId).ToList();
                var pendingBuildings = new List<PendingBuildingPortal>();

                foreach (var group in buildingGroups) {
                    // Build triangle data from portal polygons (triangle fan per polygon)
                    var triangleVertices = new List<Vector3>();
                    var buildingMin = new Vector3(float.MaxValue);
                    var buildingMax = new Vector3(float.MinValue);

                    foreach (var portal in group.Portals) {
                        // Transform the portal vertices the same way as the debug portals
                        var verts = portal.Vertices.Select(v => v + lbOrigin).ToArray();

                        // Accumulate building bounding box
                        foreach (var v in verts) {
                            buildingMin = Vector3.Min(buildingMin, v);
                            buildingMax = Vector3.Max(buildingMax, v);
                        }

                        // Triangle fan: vertex 0 is the hub
                        for (int i = 1; i < verts.Length - 1; i++) {
                            triangleVertices.Add(verts[0]);
                            triangleVertices.Add(verts[i]);
                            triangleVertices.Add(verts[i + 1]);
                        }
                    }

                    pendingBuildings.Add(new PendingBuildingPortal {
                        BuildingIndex = group.BuildingIndex,
                        Vertices = triangleVertices.ToArray(),
                        BoundingBox = new BoundingBox(buildingMin, buildingMax),
                        EnvCellIds = group.EnvCellIds
                    });
                }

                lb.PendingPortals = portals;
                lb.PendingBoundingBox = new BoundingBox(lbMin, lbMax);
                lb.PendingBuildings = pendingBuildings;
                _uploadQueue.Enqueue(lb);
            }
            catch (Exception ex) {
                _log.LogError(ex, "Error generating portals for landblock ({X},{Y})", lb.GridX, lb.GridY);
            }
        }

        private unsafe void UploadLandblock(PortalLandblock lb) {
            lb.Portals.Clear();

            // Clean up old building GPU resources
            foreach (var building in lb.BuildingPortals) {
                if (building.VAO != 0) _gl.DeleteVertexArray(building.VAO);
                if (building.VBO != 0) {
                    GpuMemoryTracker.TrackDeallocation(building.VertexCount * sizeof(Vector3), GpuResourceType.Buffer);
                    _gl.DeleteBuffer(building.VBO);
                }
                if (building.QueryId != 0) _gl.DeleteQuery(building.QueryId);
            }
            lb.BuildingPortals.Clear();

            if (lb.PendingPortals != null) {
                lb.Portals.AddRange(lb.PendingPortals);
                lb.PendingPortals = null;
                lb.BoundingBox = lb.PendingBoundingBox;
            }

            // Upload building portal mesh data for stencil rendering
            if (lb.PendingBuildings != null) {
                foreach (var pending in lb.PendingBuildings) {
                    uint vao = 0, vbo = 0;

                    if (pending.Vertices.Length > 0) {
                        vao = _gl.GenVertexArray();
                        vbo = _gl.GenBuffer();

                        _gl.BindVertexArray(vao);
                        _gl.BindBuffer(GLEnum.ArrayBuffer, vbo);

                        fixed (Vector3* ptr = pending.Vertices) {
                            _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(pending.Vertices.Length * sizeof(Vector3)), ptr, GLEnum.StaticDraw);
                        }

                        // aPosition at location 0
                        _gl.EnableVertexAttribArray(0);
                        _gl.VertexAttribPointer(0, 3, GLEnum.Float, false, (uint)sizeof(Vector3), (void*)0);

                        _gl.BindVertexArray(0);

                        GpuMemoryTracker.TrackAllocation(pending.Vertices.Length * sizeof(Vector3), GpuResourceType.Buffer);
                    }

                    lb.BuildingPortals.Add(new BuildingPortalGPU {
                        BuildingIndex = pending.BuildingIndex,
                        VAO = vao,
                        VBO = vbo,
                        VertexCount = pending.Vertices.Length,
                        BoundingBox = pending.BoundingBox,
                        EnvCellIds = pending.EnvCellIds,
                        QueryId = _gl.GenQuery()
                    });
                }
                lb.PendingBuildings = null;
            }

            lb.Ready = true;
        }

        private unsafe void UnloadLandblock(PortalLandblock lb) {
            lb.Portals.Clear();

            foreach (var building in lb.BuildingPortals) {
                if (building.VAO != 0) {
                    _gl.DeleteVertexArray(building.VAO);
                }
                if (building.VBO != 0) {
                    GpuMemoryTracker.TrackDeallocation(building.VertexCount * sizeof(Vector3), GpuResourceType.Buffer);
                    _gl.DeleteBuffer(building.VBO);
                }
                if (building.QueryId != 0) {
                    _gl.DeleteQuery(building.QueryId);
                }
            }
            lb.BuildingPortals.Clear();

            lb.Ready = false;
        }

        public unsafe void Dispose() {
            _landscapeDoc.LandblockChanged -= OnLandblockChanged;
            foreach (var lb in _landblocks.Values) {
                UnloadLandblock(lb);
            }
            _landblocks.Clear();
        }

        #region Inner Types

        private class PortalLandblock {
            public int GridX;
            public int GridY;
            public List<WorldBuilder.Shared.Services.PortalData> Portals = new();
            public List<WorldBuilder.Shared.Services.PortalData>? PendingPortals;
            public BoundingBox BoundingBox;
            public BoundingBox PendingBoundingBox;
            public bool Ready;

            // Per-building GPU portal mesh data
            public List<BuildingPortalGPU> BuildingPortals = new();
            public List<PendingBuildingPortal>? PendingBuildings;
        }

        private class PendingBuildingPortal {
            public int BuildingIndex;
            public Vector3[] Vertices = Array.Empty<Vector3>();
            public BoundingBox BoundingBox;
            public HashSet<uint> EnvCellIds = new();
        }

        #endregion
    }

    /// <summary>
    /// GPU-uploaded portal polygon mesh for a single building.
    /// Used for stencil-based portal rendering.
    /// </summary>
    internal sealed class BuildingPortalGPU {
        public int BuildingIndex { get; init; }
        public uint VAO { get; init; }
        public uint VBO { get; init; }
        public int VertexCount { get; init; }
        public BoundingBox BoundingBox { get; init; }
        public HashSet<uint> EnvCellIds { get; init; } = new();
        public uint QueryId { get; set; }
        public bool QueryStarted { get; set; }
        public bool WasVisible { get; set; } = true;
    }
}
