using Chorizite.Core.Lib;
using Chorizite.Core.Render;
using DatReaderWriter;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Services;

namespace Chorizite.OpenGLSDLBackend.Lib {
    public class TerrainRenderManager : IDisposable {
        private readonly GL _gl;
        private readonly ILogger _log;
        private readonly LandscapeDocument _landscapeDoc;
        private readonly ConcurrentDictionary<ushort, TerrainChunk> _chunks = new();

        // Job queues
        private readonly ConcurrentDictionary<ushort, TerrainChunk> _pendingGeneration = new();
        private readonly ConcurrentQueue<TerrainChunk> _uploadQueue = new();
        private readonly ConcurrentQueue<TerrainChunk> _partialUpdateQueue = new();
        private readonly ConcurrentQueue<TerrainChunk> _readyForUploadQueue = new();
        private readonly ConcurrentDictionary<TerrainChunk, byte> _queuedForPartialUpdate = new();
        private int _activeGenerations = 0;
        private int _activePartialUpdates = 0;

        // Constants
        private const int MaxVertices = 16384;
        private const int MaxIndices = 24576;
        private const int LandblocksPerChunk = 8;
        private float _chunkSizeInUnits;

        // Render state
        private IShader? _shader;
        private bool _initialized;

        // Statistics
        public int RenderDistance { get; set; } = 12;
        public int QueuedUploads => _uploadQueue.Count;
        public int QueuedGenerations => _pendingGeneration.Count;
        public int QueuedPartialUpdates => _partialUpdateQueue.Count;
        public int ActiveChunks => _chunks.Count;

        // Brush settings
        public Vector3 BrushPosition { get; set; }
        public float BrushRadius { get; set; } = 30f;
        public Vector4 BrushColor { get; set; } = new Vector4(0.0f, 1.0f, 0.0f, 0.4f);
        public bool ShowBrush { get; set; }
        public int BrushShape { get; set; }

        // Grid settings
        public bool ShowLandblockGrid { get; set; }
        public bool ShowCellGrid { get; set; }
        public Vector3 LandblockGridColor { get; set; }
        public Vector3 CellGridColor { get; set; }
        public float GridLineWidth { get; set; } = 1.0f;
        public float GridOpacity { get; set; } = 1.0f;
        public float ScreenHeight { get; set; } = 1080.0f;

        private readonly Frustum _frustum = new();
        private readonly IDatReaderWriter _dats;
        private readonly OpenGLGraphicsDevice _graphicsDevice;
        private LandSurfaceManager? _surfaceManager;

        public TerrainRenderManager(GL gl, ILogger log, LandscapeDocument landscapeDoc, IDatReaderWriter dats,
            OpenGLGraphicsDevice graphicsDevice) {
            _gl = gl;
            _log = log;
            _landscapeDoc = landscapeDoc;
            _dats = dats;
            _graphicsDevice = graphicsDevice;
            log.LogTrace($"Initialized TerrainRenderManager");

            _landscapeDoc.LandblockChanged += OnLandblockChanged;
        }

        private void OnLandblockChanged(object? sender, LandblockChangedEventArgs e) {
            if (e.AffectedLandblocks == null) {
                _log.LogTrace("LandblockChanged: All landblocks invalidated");
                InvalidateLandblock(-1, -1);
            }
            else {
                var affected = e.AffectedLandblocks.ToList();
                _log.LogTrace("LandblockChanged: {Count} landblocks affected: {Landblocks}",
                    affected.Count, string.Join(", ", affected.Select(lb => $"({lb.x}, {lb.y})")));
                foreach (var (lbX, lbY) in affected) {
                    InvalidateLandblock(lbX, lbY);
                }
            }
        }

        public void Initialize(IShader shader) {
            _shader = shader;
            _initialized = true;

            // Initialize Surface Manager
            if (_landscapeDoc.Region is RegionInfo regionInfo) {
                _surfaceManager = new LandSurfaceManager(_graphicsDevice, _dats, regionInfo._region, _log);
                _chunkSizeInUnits = regionInfo.LandblockSizeInUnits * LandblocksPerChunk;
            }
        }

        public void Update(float deltaTime, ICamera camera) {
            if (!_initialized) return;

            _frustum.Update(camera.ViewProjectionMatrix);

            if (_landscapeDoc.Region is null) return;

            // Calculate current chunk
            var pos = new Vector2(camera.Position.X, camera.Position.Y) - _landscapeDoc.Region.MapOffset;
            var chunkX = (int)Math.Floor(pos.X / _chunkSizeInUnits);
            var chunkY = (int)Math.Floor(pos.Y / _chunkSizeInUnits);

            // Queue new chunks
            for (int x = chunkX - RenderDistance; x <= chunkX + RenderDistance; x++) {
                for (int y = chunkY - RenderDistance; y <= chunkY + RenderDistance; y++) {
                    if (x < 0 || y < 0) continue;

                    // Skip chunks outside the camera frustum (using estimated bounds)
                    if (!IsChunkInFrustum(x, y)) continue;

                    var uX = (uint)x;
                    var uY = (uint)y;

                    var chunkId = (ushort)((uX << 8) | uY);
                    if (!_chunks.ContainsKey(chunkId)) {
                        var chunk = new TerrainChunk(uX, uY);
                        if (_chunks.TryAdd(chunkId, chunk)) {
                            _pendingGeneration[chunkId] = chunk;
                        }
                    }
                }
            }

            // Clean up chunks that are no longer in frustum and not yet loaded
            foreach (var (key, chunk) in _chunks) {
                if (!chunk.IsGenerated && !IsChunkInFrustum((int)chunk.ChunkX, (int)chunk.ChunkY)) {
                    if (_chunks.TryRemove(key, out _)) {
                        _pendingGeneration.TryRemove(key, out _);
                        chunk.Dispose();
                    }
                }
            }

            while (_activeGenerations < 12 && !_pendingGeneration.IsEmpty) {
                // Pick the nearest pending chunk (Chebyshev distance)
                TerrainChunk? nearest = null;
                int bestDist = int.MaxValue;
                ushort bestKey = 0;

                foreach (var (key, chunk) in _pendingGeneration) {
                    var dist = Math.Max(Math.Abs((int)chunk.ChunkX - chunkX), Math.Abs((int)chunk.ChunkY - chunkY));
                    if (dist < bestDist) {
                        bestDist = dist;
                        nearest = chunk;
                        bestKey = key;
                    }
                }

                if (nearest == null || !_pendingGeneration.TryRemove(bestKey, out var chunkToGenerate))
                    break;

                // Skip if now out of range or not in frustum
                if (bestDist > RenderDistance || !IsChunkInFrustum((int)chunkToGenerate.ChunkX, (int)chunkToGenerate.ChunkY)) {
                    if (_chunks.TryRemove(bestKey, out _)) {
                        chunkToGenerate.Dispose();
                    }
                    continue;
                }

                System.Threading.Interlocked.Increment(ref _activeGenerations);
                Task.Run(() => {
                    try {
                        GenerateChunk(chunkToGenerate);
                    }
                    finally {
                        System.Threading.Interlocked.Decrement(ref _activeGenerations);
                    }
                });
            }
        }

        private bool IsChunkInFrustum(int chunkX, int chunkY) {
            var offset = _landscapeDoc.Region?.MapOffset ?? Vector2.Zero;
            var minX = chunkX * _chunkSizeInUnits + offset.X;
            var minY = chunkY * _chunkSizeInUnits + offset.Y;
            var maxX = (chunkX + 1) * _chunkSizeInUnits + offset.X;
            var maxY = (chunkY + 1) * _chunkSizeInUnits + offset.Y;

            var box = new BoundingBox(
                new Vector3(minX, minY, -1000f),
                new Vector3(maxX, maxY, 5000f)
            );
            return _frustum.Intersects(box);
        }

        private bool IsWithinRenderDistance(TerrainChunk chunk, int cameraChunkX, int cameraChunkY) {
            return Math.Abs((int)chunk.ChunkX - cameraChunkX) <= RenderDistance
                && Math.Abs((int)chunk.ChunkY - cameraChunkY) <= RenderDistance;
        }

        public float ProcessUploads(float timeBudgetMs) {
            if (!_initialized) return 0;

            var sw = Stopwatch.StartNew();

            // Background generation of partial updates
            DispatchPartialUpdates();

            // Prioritize partial updates for responsiveness (Main thread GPU upload)
            ApplyPartialUpdates(sw, timeBudgetMs);

            while (_uploadQueue.TryPeek(out var chunk)) {
                if (sw.Elapsed.TotalMilliseconds > timeBudgetMs) {
                    break;
                }

                if (_uploadQueue.TryDequeue(out chunk)) {
                    // Skip if this chunk is no longer in frustum
                    if (!IsChunkInFrustum((int)chunk.ChunkX, (int)chunk.ChunkY)) {
                        var chunkId = (ushort)((chunk.ChunkX << 8) | chunk.ChunkY);
                        if (_chunks.TryRemove(chunkId, out _)) {
                            chunk.Dispose();
                        }
                        continue;
                    }
                    UploadChunk(chunk);
                }
            }

            return (float)sw.Elapsed.TotalMilliseconds;
        }

        private void DispatchPartialUpdates() {
            while (_activePartialUpdates < 8 && _partialUpdateQueue.TryDequeue(out var chunk)) {
                System.Threading.Interlocked.Increment(ref _activePartialUpdates);
                Task.Run(() => {
                    try {
                        ProcessChunkUpdate(chunk);
                    }
                    finally {
                        System.Threading.Interlocked.Decrement(ref _activePartialUpdates);
                    }
                });
            }
        }

        private void ProcessChunkUpdate(TerrainChunk chunk) {
            try {
                // Temporary buffers for single landblock
                var tempVertices = new VertexLandscape[TerrainGeometryGenerator.VerticesPerLandblock];
                var tempIndices = new uint[TerrainGeometryGenerator.IndicesPerLandblock]; // Unused but required by signature

                while (chunk.TryGetNextDirty(out int lx, out int ly)) {
                    int vertexOffset = chunk.LandblockVertexOffsets[ly * 8 + lx];
                    if (vertexOffset == -1) continue; // No geometry for this block

                    var landblockX = chunk.LandblockStartX + (uint)lx;
                    var landblockY = chunk.LandblockStartY + (uint)ly;

                    if (_landscapeDoc.Region is null) continue;

                    var landblockID = _landscapeDoc.Region.GetLandblockId((int)landblockX, (int)landblockY);

                    var (lbMinZ, lbMaxZ) = TerrainGeometryGenerator.GenerateLandblockGeometry(
                        landblockX, landblockY, landblockID,
                        _landscapeDoc.Region, _surfaceManager!,
                        _landscapeDoc.TerrainCache.AsSpan(),
                        0, 0,
                        tempVertices, tempIndices
                    );

                    var update = new PendingPartialUpdate {
                        LocalX = lx,
                        LocalY = ly,
                        Vertices = tempVertices.ToArray(),
                        MinZ = lbMinZ,
                        MaxZ = lbMaxZ
                    };
                    chunk.PendingPartialUpdates.Enqueue(update);
                }

                _readyForUploadQueue.Enqueue(chunk);
            }
            catch (Exception ex) {
                _log.LogError(ex, "Error processing partial update for chunk {CX},{CY}", chunk.ChunkX, chunk.ChunkY);
            }
            finally {
                _queuedForPartialUpdate.TryRemove(chunk, out _);
            }
        }

        private unsafe void ApplyPartialUpdates(Stopwatch sw, float timeBudgetMs) {
            int initialCount = _readyForUploadQueue.Count;
            int processed = 0;

            while (processed < initialCount && sw.Elapsed.TotalMilliseconds < timeBudgetMs) {
                if (_readyForUploadQueue.TryDequeue(out var chunk)) {
                    _gl.BindVertexArray(chunk.VAO);
                    _gl.BindBuffer(BufferTargetARB.ArrayBuffer, chunk.VBO);

                    bool boundsChanged = false;
                    while (chunk.PendingPartialUpdates.TryDequeue(out var update)) {
                        int vertexOffset = chunk.LandblockVertexOffsets[update.LocalY * 8 + update.LocalX];
                        if (vertexOffset == -1) continue;

                        chunk.LandblockBoundsMinZ[update.LocalY * 8 + update.LocalX] = update.MinZ;
                        chunk.LandblockBoundsMaxZ[update.LocalY * 8 + update.LocalX] = update.MaxZ;
                        boundsChanged = true;

                        // Upload vertices
                        fixed (VertexLandscape* vPtr = update.Vertices) {
                            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, (nint)(vertexOffset * VertexLandscape.Size), (nuint)(update.Vertices.Length * VertexLandscape.Size), vPtr);
                        }

                        if (sw.Elapsed.TotalMilliseconds > timeBudgetMs) {
                            break;
                        }
                    }

                    if (boundsChanged) {
                        float minZ = float.MaxValue;
                        float maxZ = float.MinValue;
                        for (int i = 0; i < 64; i++) {
                            if (chunk.LandblockVertexOffsets[i] != -1) {
                                minZ = Math.Min(minZ, chunk.LandblockBoundsMinZ[i]);
                                maxZ = Math.Max(maxZ, chunk.LandblockBoundsMaxZ[i]);
                            }
                        }
                        var offset = _landscapeDoc.Region?.MapOffset ?? Vector2.Zero;
                        chunk.Bounds = new BoundingBox(
                            new Vector3(new Vector2(chunk.ChunkX * 8 * 192f, chunk.ChunkY * 8 * 192f) + offset, minZ),
                            new Vector3(new Vector2((chunk.ChunkX + 1) * 8 * 192f, (chunk.ChunkY + 1) * 8 * 192f) + offset, maxZ)
                        );
                    }

                    // If we still have pending updates for this chunk (because we hit the budget), put it back in the queue
                    if (!chunk.PendingPartialUpdates.IsEmpty) {
                        _readyForUploadQueue.Enqueue(chunk);
                    }
                    processed++;
                }
                else {
                    break;
                }
            }
        }

        private void GenerateChunk(TerrainChunk chunk) {
            try {
                var vertices = new VertexLandscape[MaxVertices];
                var indices = new uint[MaxIndices];
                int vCount = 0;
                int iCount = 0;

                if (_landscapeDoc.Region != null) {
                    TerrainGeometryGenerator.GenerateChunkGeometry(
                        chunk,
                        _landscapeDoc.Region,
                        _surfaceManager!,
                        _landscapeDoc.TerrainCache,
                        vertices,
                        indices,
                        out vCount,
                        out iCount
                    );
                }
                else {
                    _log.LogWarning("Cannot generate chunk {CX},{CY}: Region is null", chunk.ChunkX, chunk.ChunkY);
                }

                if (vCount > 0) {
                    chunk.GeneratedVertices = vertices.AsMemory(0, vCount);
                    chunk.GeneratedIndices = indices.AsMemory(0, iCount);
                }

                _uploadQueue.Enqueue(chunk);
            }
            catch (Exception ex) {
                _log.LogError(ex, "Error generating chunk {CX},{CY}", chunk.ChunkX, chunk.ChunkY);
            }
        }

        private unsafe void UploadChunk(TerrainChunk chunk) {
            if (chunk.GeneratedVertices.Length == 0) {
                //_log.LogWarning("Skipping upload for chunk {CX},{CY}: No vertices", chunk.ChunkX, chunk.ChunkY);
                return;
            }

            var vertices = chunk.GeneratedVertices.Span;
            var indices = chunk.GeneratedIndices.Span;

            chunk.VAO = _gl.GenVertexArray();
            _gl.BindVertexArray(chunk.VAO);

            chunk.VBO = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, chunk.VBO);

            fixed (VertexLandscape* vPtr = vertices) {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * VertexLandscape.Size), vPtr,
                    BufferUsageARB.StaticDraw);
            }

            chunk.EBO = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, chunk.EBO);

            fixed (uint* iPtr = indices) {
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), iPtr,
                    BufferUsageARB.StaticDraw);
            }

            // Set up attributes based on VertexLandscape.Format
            // 0: Pos (3 float)
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (uint)VertexLandscape.Size, (void*)VertexLandscape.OffsetPosition);

            // 1: Normal (3 float)
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, (uint)VertexLandscape.Size, (void*)VertexLandscape.OffsetNormal);

            // 2: TexCoord0 (4 byte)
            _gl.EnableVertexAttribArray(2);
            _gl.VertexAttribIPointer(2, 4, VertexAttribIType.UnsignedByte, (uint)VertexLandscape.Size, (void*)VertexLandscape.OffsetTexCoord0);

            // 3: PackedOverlay0 (4 byte)
            _gl.EnableVertexAttribArray(3);
            _gl.VertexAttribIPointer(3, 4, VertexAttribIType.UnsignedByte, (uint)VertexLandscape.Size, (void*)VertexLandscape.OffsetTexCoord1);

            // 4: PackedOverlay1 (4 byte)
            _gl.EnableVertexAttribArray(4);
            _gl.VertexAttribIPointer(4, 4, VertexAttribIType.UnsignedByte, (uint)VertexLandscape.Size, (void*)VertexLandscape.OffsetTexCoord2);

            // 5: PackedOverlay2 (4 byte)
            _gl.EnableVertexAttribArray(5);
            _gl.VertexAttribIPointer(5, 4, VertexAttribIType.UnsignedByte, (uint)VertexLandscape.Size, (void*)VertexLandscape.OffsetTexCoord3);

            // 6: PackedRoad0 (4 byte)
            _gl.EnableVertexAttribArray(6);
            _gl.VertexAttribIPointer(6, 4, VertexAttribIType.UnsignedByte, (uint)VertexLandscape.Size, (void*)VertexLandscape.OffsetTexCoord4);

            // 7: PackedRoad1 (4 byte)
            _gl.EnableVertexAttribArray(7);
            _gl.VertexAttribIPointer(7, 4, VertexAttribIType.UnsignedByte, (uint)VertexLandscape.Size, (void*)VertexLandscape.OffsetTexCoord5);

            chunk.IndexCount = indices.Length;
            chunk.IsGenerated = true;

            // Clear cpu memory
            chunk.GeneratedVertices = Memory<VertexLandscape>.Empty;
            chunk.GeneratedIndices = Memory<uint>.Empty;

            _gl.BindVertexArray(0);
        }

        public unsafe void Render(ICamera camera) {
            if (!_initialized || _shader is null) return;

            _shader.Bind();

            // Set uniforms
            _shader.SetUniform("xView", camera.ViewMatrix);
            _shader.SetUniform("xProjection", camera.ProjectionMatrix);
            _shader.SetUniform("xWorld", Matrix4x4.Identity); // Chunks are already in world space coordinates
            _shader.SetUniform("uAlpha", 1.0f);
            _shader.SetUniform("xAmbient", 0.5f); // 0.5 ambient

            // Brush uniforms
            _shader.SetUniform("uBrushPos", BrushPosition);
            _shader.SetUniform("uBrushRadius", BrushRadius);
            _shader.SetUniform("uBrushColor", BrushColor);
            _shader.SetUniform("uShowBrush", ShowBrush ? 1 : 0);
            _shader.SetUniform("uBrushShape", BrushShape);

            // Grid uniforms
            _shader.SetUniform("uShowLandblockGrid", ShowLandblockGrid ? 1 : 0);
            _shader.SetUniform("uShowCellGrid", ShowCellGrid ? 1 : 0);
            _shader.SetUniform("uLandblockGridColor", LandblockGridColor);
            _shader.SetUniform("uCellGridColor", CellGridColor);
            _shader.SetUniform("uGridLineWidth", GridLineWidth);
            _shader.SetUniform("uGridOpacity", GridOpacity);
            _shader.SetUniform("uScreenHeight", ScreenHeight);

            float camDist = Math.Abs(camera.Position.Z);
            _shader.SetUniform("uCameraDistance", camDist < 1f ? 1f : camDist);

            if (_surfaceManager != null) {
                _surfaceManager.TerrainAtlas.Bind(0);
                _shader.SetUniform("xOverlays", 0);

                _surfaceManager.AlphaAtlas.Bind(1);
                _shader.SetUniform("xAlphas", 1);
            }

            _frustum.Update(camera.ViewProjectionMatrix);

            foreach (var chunk in _chunks.Values) {
                if (!chunk.IsGenerated || chunk.IndexCount == 0) continue;

                if (!_frustum.Intersects(chunk.Bounds)) continue;

                _gl.BindVertexArray(chunk.VAO);
                _gl.DrawElements(PrimitiveType.Triangles, (uint)chunk.IndexCount, DrawElementsType.UnsignedInt,
                    (void*)0);
            }

            _gl.BindVertexArray(0);
        }

        public void InvalidateLandblock(int lbX, int lbY) {
            if (lbX == -1 && lbY == -1) {
                foreach (var c in _chunks.Values) {
                    if (c.IsGenerated) {
                        c.MarkAllDirty();
                        if (_queuedForPartialUpdate.TryAdd(c, 1)) {
                            _partialUpdateQueue.Enqueue(c);
                        }
                    }
                }
                return;
            }

            var chunkX = (uint)(lbX / 8);
            var chunkY = (uint)(lbY / 8);
            var chunkId = (ushort)((chunkX << 8) | chunkY);

            if (_chunks.TryGetValue(chunkId, out var chunk)) {
                if (chunk.IsGenerated) {
                    chunk.MarkDirty(lbX % 8, lbY % 8);
                    if (_queuedForPartialUpdate.TryAdd(chunk, 1)) {
                        _partialUpdateQueue.Enqueue(chunk);
                    }
                }
                else {
                    // Fallback to full regen if not ready
                    if (_chunks.TryRemove(chunkId, out var _)) {
                        _pendingGeneration.TryRemove(chunkId, out _);
                        chunk.Dispose();
                    }
                }
            }
        }

        public void Dispose() {
            _landscapeDoc.LandblockChanged -= OnLandblockChanged;
            foreach (var chunk in _chunks.Values) {
                chunk.Dispose();
            }

            _chunks.Clear();
            _pendingGeneration.Clear();
        }
    }
}