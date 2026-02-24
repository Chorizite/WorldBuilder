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
using WorldBuilder.Shared.Numerics;
using WorldBuilder.Shared.Services;
using VertexAttribType = Chorizite.Core.Render.Enums.VertexAttribType;
using BufferUsage = Chorizite.Core.Render.Enums.BufferUsage;
using PrimitiveType = Silk.NET.OpenGL.PrimitiveType;

namespace Chorizite.OpenGLSDLBackend.Lib {
    /// <summary>
    /// Represents a vertex for portal rendering.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PortalVertex : IVertex {
        public Vector3 Position;
        public Vector4 Color;

        public static int Size => Marshal.SizeOf<PortalVertex>();

        public static VertexFormat Format => new VertexFormat(
            new VertexAttribute(VertexAttributeName.Position, 3, VertexAttribType.Float, false, (int)Marshal.OffsetOf<PortalVertex>(nameof(Position))),
            new VertexAttribute(VertexAttributeName.Color, 4, VertexAttribType.Float, false, (int)Marshal.OffsetOf<PortalVertex>(nameof(Color)))
        );
    }

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

        private IShader? _shader;
        private bool _initialized;

        public bool ShowPortals { get; set; } = true;
        public int RenderDistance { get; set; } = 12;

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
                    lb.GpuReady = false;
                    var key = GeometryUtils.PackKey(lb.GridX, lb.GridY);
                    _pendingGeneration[key] = lb;
                }
            }
            else {
                foreach (var (lbX, lbY) in e.AffectedLandblocks) {
                    var key = GeometryUtils.PackKey(lbX, lbY);
                    if (_landblocks.TryGetValue(key, out var lb)) {
                        lb.GpuReady = false;
                        _pendingGeneration[key] = lb;
                    }
                }
            }
        }

        public void Initialize(IShader shader) {
            _shader = shader;
            _initialized = true;
        }

        public void Update(float deltaTime, ICamera camera) {
            if (!_initialized || _landscapeDoc.Region == null) return;

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
            if (!_initialized) return 0;

            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalMilliseconds < timeBudgetMs && _uploadQueue.TryDequeue(out var lb)) {
                UploadLandblock(lb);
            }
            return (float)sw.Elapsed.TotalMilliseconds;
        }

        public void Render(Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix) {
            if (!_initialized || !ShowPortals || _shader == null) return;

            _shader.Bind();
            _shader.SetUniform("uView", viewMatrix);
            _shader.SetUniform("uProjection", projectionMatrix);
            _shader.SetUniform("uModel", Matrix4x4.Identity);

            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            _gl.DepthMask(false);

            foreach (var lb in _landblocks.Values) {
                if (!lb.GpuReady) continue;

                foreach (var portal in lb.Portals) {
                    portal.VAO.Bind();
                    _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)portal.VertexCount);
                }
            }

            _gl.DepthMask(true);
            _gl.BindVertexArray(0);
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
                        foreach (var portal in envCell.CellPortals) {
                            if (portal.OtherCellId == 0xFFFF) {
                                // Portal to outside!
                                if (_dats.Portal.TryGet<DatReaderWriter.DBObjs.Environment>(0x0D000000u | envCell.EnvironmentId, out var environment)) {
                                    if (environment.Cells.TryGetValue(envCell.CellStructure, out var cellStruct)) {
                                        if (cellStruct.Polygons.TryGetValue(portal.PolygonId, out var polygon)) {
                                            var vertexData = new List<PortalVertex>();
                                            var color = new Vector4(1f, 0f, 1f, 0.5f); // Magenta, semi-transparent

                                            // Triangulate the polygon
                                            for (int j = 1; j < polygon.VertexIds.Count - 1; j++) {
                                                AddVertex(vertexData, cellStruct.VertexArray, polygon.VertexIds[0], color);
                                                AddVertex(vertexData, cellStruct.VertexArray, polygon.VertexIds[j], color);
                                                AddVertex(vertexData, cellStruct.VertexArray, polygon.VertexIds[j + 1], color);
                                            }

                                            var transform = Matrix4x4.CreateFromQuaternion(envCell.Position.Orientation) *
                                                            Matrix4x4.CreateTranslation(envCell.Position.Origin) *
                                                            Matrix4x4.CreateTranslation(lbOrigin);

                                            portals.Add(new PortalData {
                                                Vertices = vertexData.Select(v => new PortalVertex {
                                                    Position = Vector3.Transform(v.Position, transform),
                                                    Color = v.Color
                                                }).ToArray()
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

        private void AddVertex(List<PortalVertex> list, DatReaderWriter.Types.VertexArray vertexArray, short vertexId, Vector4 color) {
            if (vertexArray.Vertices.TryGetValue((ushort)vertexId, out var vertex)) {
                list.Add(new PortalVertex {
                    Position = vertex.Origin,
                    Color = color
                });
            }
        }

        private void UploadLandblock(PortalLandblock lb) {
            foreach (var portal in lb.Portals) {
                portal.Dispose();
            }
            lb.Portals.Clear();

            if (lb.PendingPortals != null) {
                foreach (var data in lb.PendingPortals) {
                    var vbo = new ManagedGLVertexBuffer(_graphicsDevice, BufferUsage.Static, data.Vertices.Length * PortalVertex.Size);
                    vbo.SetData(data.Vertices);
                    var vao = new ManagedGLVertexArray(_graphicsDevice, vbo, PortalVertex.Format);
                    lb.Portals.Add(new PortalGpuData {
                        VBO = vbo,
                        VAO = vao,
                        VertexCount = data.Vertices.Length
                    });
                }
                lb.PendingPortals = null;
            }
            lb.GpuReady = true;
        }

        private void UnloadLandblock(PortalLandblock lb) {
            foreach (var portal in lb.Portals) {
                portal.Dispose();
            }
            lb.Portals.Clear();
            lb.GpuReady = false;
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
            public List<PortalGpuData> Portals = new();
            public List<PortalData>? PendingPortals;
            public bool GpuReady;
        }

        private class PortalData {
            public PortalVertex[] Vertices = Array.Empty<PortalVertex>();
        }

        private class PortalGpuData : IDisposable {
            public ManagedGLVertexBuffer VBO = null!;
            public ManagedGLVertexArray VAO = null!;
            public int VertexCount;

            public void Dispose() {
                VBO?.Dispose();
                VAO?.Dispose();
            }
        }
    }
}
