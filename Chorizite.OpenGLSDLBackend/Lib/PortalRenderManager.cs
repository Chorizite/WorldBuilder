using Chorizite.Core.Lib;
using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
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

namespace Chorizite.OpenGLSDLBackend.Lib {
    /// <summary>
    /// Manages portal rendering.
    /// Portals are semi-transparent magenta polygons that connect cells to the outside world.
    /// </summary>
    public class PortalRenderManager : IDisposable {
        private readonly GL _gl;
        private readonly ILogger _log;
        private readonly LandscapeDocument _landscapeDoc;
        private readonly IDatReaderWriter _dats;
        private readonly OpenGLGraphicsDevice _graphicsDevice;

        // Per-landblock portal data
        private readonly ConcurrentDictionary<ushort, PortalLandblock> _landblocks = new();
        private readonly ConcurrentDictionary<ushort, PortalLandblock> _pendingGeneration = new();
        private readonly ConcurrentQueue<PortalLandblock> _uploadQueue = new();
        private int _activeGenerations = 0;

        public bool ShowPortals { get; set; } = true;
        public int RenderDistance { get; set; } = 12;

        public (uint CellId, uint PortalIndex)? HoveredPortal { get; set; }
        public (uint CellId, uint PortalIndex)? SelectedPortal { get; set; }

        private Vector3 _cameraPosition;
        private int _cameraLbX;
        private int _cameraLbY;
        private float _lbSizeInUnits;
        private readonly Frustum _frustum = new();

        public PortalRenderManager(GL gl, ILogger log, LandscapeDocument landscapeDoc,
            IDatReaderWriter dats, OpenGLGraphicsDevice graphicsDevice) {
            _gl = gl;
            _log = log;
            _landscapeDoc = landscapeDoc;
            _dats = dats;
            _graphicsDevice = graphicsDevice;

            _landscapeDoc.LandblockChanged += OnLandblockChanged;
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

            _frustum.Update(camera.ViewProjectionMatrix);

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

            var magenta = new Vector4(1f, 0f, 1f, 1f);
            var hoverColor = LandscapeColorsSettings.Instance.Hover;
            var selectionColor = LandscapeColorsSettings.Instance.Selection;

            foreach (var lb in _landblocks.Values) {
                if (!lb.Ready) continue;

                foreach (var portal in lb.Portals) {
                    var color = magenta;
                    if (HoveredPortal.HasValue && HoveredPortal.Value.CellId == portal.CellId && HoveredPortal.Value.PortalIndex == portal.PortalIndex) {
                        color = hoverColor;
                    }
                    if (SelectedPortal.HasValue && SelectedPortal.Value.CellId == portal.CellId && SelectedPortal.Value.PortalIndex == portal.PortalIndex) {
                        color = selectionColor;
                    }

                    for (int i = 0; i < portal.Vertices.Length; i++) {
                        debug.DrawLine(portal.Vertices[i], portal.Vertices[(i + 1) % portal.Vertices.Length], color, 5.0f);
                    }
                }
            }
        }

        public bool Raycast(Vector3 rayOrigin, Vector3 rayDirection, out SceneRaycastHit hit) {
            hit = SceneRaycastHit.NoHit;
            if (!ShowPortals || _landscapeDoc.Region == null) return false;

            float closestDistance = float.MaxValue;
            PortalData? closestPortal = null;
            uint closestLandblockId = 0;

            foreach (var lb in _landblocks.Values) {
                if (!lb.Ready) continue;

                var lbGlobalX = (uint)lb.GridX;
                var lbGlobalY = (uint)lb.GridY;
                var lbId = (uint)((lbGlobalX << 24) | (lbGlobalY << 16) | 0xFFFF);

                foreach (var portal in lb.Portals) {
                    if (RaycastingUtils.RayIntersectsPolygon(rayOrigin, rayDirection, portal.Vertices, out float distance)) {
                        if (distance < closestDistance) {
                            closestDistance = distance;
                            closestPortal = portal;
                            closestLandblockId = lbId;
                        }
                    }
                }
            }

            if (closestPortal != null) {
                hit = new SceneRaycastHit {
                    Hit = true,
                    Type = InspectorSelectionType.Portal,
                    Distance = closestDistance,
                    Position = rayOrigin + rayDirection * closestDistance,
                    LandblockId = closestLandblockId,
                    ObjectId = closestPortal.CellId,
                    InstanceId = closestPortal.PortalIndex
                };
                return true;
            }

            return false;
        }

        private async Task GeneratePortalsForLandblock(PortalLandblock lb) {
            try {
                var lbGlobalX = (uint)lb.GridX;
                var lbGlobalY = (uint)lb.GridY;
                var lbId = (uint)((lbGlobalX << 24) | (lbGlobalY << 16) | 0xFFFE);
                var lbiId = (lbId & 0xFFFF0000) | 0xFFFE;

                if (!_dats.CellRegions.TryGetValue(_landscapeDoc.RegionId, out var cellDb)) return;

                if (!cellDb.TryGet<LandBlockInfo>(lbiId, out var lbi)) return;

                var portals = new List<PortalData>();
                var lbOrigin = new Vector3(
                    lbGlobalX * 192f + _landscapeDoc.Region!.MapOffset.X,
                    lbGlobalY * 192f + _landscapeDoc.Region!.MapOffset.Y,
                    0
                );

                for (uint i = 0; i < lbi.NumCells; i++) {
                    var cellId = (lbId & 0xFFFF0000) | (0x0100 + i);
                    if (cellDb.TryGet<EnvCell>(cellId, out var envCell)) {
                        for (int portalIdx = 0; portalIdx < envCell.CellPortals.Count; portalIdx++) {
                            var portal = envCell.CellPortals[portalIdx];
                            if (portal.OtherCellId == 0xFFFF) {
                                // Portal to outside!
                                if (_dats.Portal.TryGet<DatReaderWriter.DBObjs.Environment>(0x0D000000u | envCell.EnvironmentId, out var environment)) {
                                    if (environment.Cells.TryGetValue(envCell.CellStructure, out var cellStruct)) {
                                        if (cellStruct.Polygons.TryGetValue(portal.PolygonId, out var polygon)) {
                                            var vertices = new List<Vector3>();

                                            foreach (var vertexId in polygon.VertexIds) {
                                                if (cellStruct.VertexArray.Vertices.TryGetValue((ushort)vertexId, out var vertex)) {
                                                    vertices.Add(vertex.Origin);
                                                }
                                            }

                                            var transform = Matrix4x4.CreateFromQuaternion(envCell.Position.Orientation) *
                                                            Matrix4x4.CreateTranslation(envCell.Position.Origin) *
                                                            Matrix4x4.CreateTranslation(lbOrigin);

                                            portals.Add(new PortalData {
                                                CellId = cellId,
                                                PortalIndex = (uint)portalIdx,
                                                Vertices = vertices.Select(v => Vector3.Transform(v, transform)).ToArray()
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                lb.PendingPortals = portals;
                _uploadQueue.Enqueue(lb);
            }
            catch (Exception ex) {
                _log.LogError(ex, "Error generating portals for landblock ({X},{Y})", lb.GridX, lb.GridY);
            }
        }

        private void UploadLandblock(PortalLandblock lb) {
            lb.Portals.Clear();

            if (lb.PendingPortals != null) {
                lb.Portals.AddRange(lb.PendingPortals);
                lb.PendingPortals = null;
            }
            lb.Ready = true;
        }

        private void UnloadLandblock(PortalLandblock lb) {
            lb.Portals.Clear();
            lb.Ready = false;
        }

        public void Dispose() {
            _landscapeDoc.LandblockChanged -= OnLandblockChanged;
            foreach (var lb in _landblocks.Values) {
                UnloadLandblock(lb);
            }
            _landblocks.Clear();
        }

        private class PortalLandblock {
            public int GridX;
            public int GridY;
            public List<PortalData> Portals = new();
            public List<PortalData>? PendingPortals;
            public bool Ready;
        }

        private class PortalData {
            public uint CellId;
            public uint PortalIndex;
            public Vector3[] Vertices = Array.Empty<Vector3>();
        }
    }
}
