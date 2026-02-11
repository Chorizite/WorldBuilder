using Chorizite.Core.Render;
using Chorizite.Core.Lib;
using Silk.NET.OpenGL;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using WorldBuilder.Shared.Models;
using Microsoft.Extensions.Logging;
using DatReaderWriter;
using WorldBuilder.Shared.Services;
using System.Threading.Tasks;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace Chorizite.OpenGLSDLBackend.Lib {
    public class TerrainRenderManager : IDisposable {
        private readonly GL _gl;
        private readonly ILogger _log;
        private readonly LandscapeDocument _landscapeDoc;
        private readonly ConcurrentDictionary<ulong, TerrainChunk> _chunks = new();

        // Job queues
        private readonly ConcurrentQueue<TerrainChunk> _generationQueue = new();
        private readonly ConcurrentQueue<TerrainChunk> _uploadQueue = new();
        private readonly ConcurrentQueue<TerrainChunk> _partialUpdateQueue = new();
        private readonly ConcurrentDictionary<TerrainChunk, byte> _queuedForPartialUpdate = new();
        private int _activeGenerations = 0;

        // Constants
        private const int MaxVertices = 16384;
        private const int MaxIndices = 24576;
        private const int LandblocksPerChunk = 8;
        private float _chunkSizeInUnits;

        // Render state
        private IShader? _shader;
        private bool _initialized;

        // Statistics
        public int RenderDistance { get; set; } = 8;
        public int QueuedUploads => _uploadQueue.Count;
        public int QueuedGenerations => _generationQueue.Count;
        public int QueuedPartialUpdates => _partialUpdateQueue.Count;
        public int ActiveChunks => _chunks.Count;

        // Brush settings
        public Vector3 BrushPosition { get; set; }
        public float BrushRadius { get; set; } = 30f;
        public Vector4 BrushColor { get; set; } = new Vector4(0.0f, 1.0f, 0.0f, 0.4f);
        public bool ShowBrush { get; set; }
        public int BrushShape { get; set; } // 0 = Circle, 1 = Square

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

            // Bind textures
            if (_surfaceManager != null) {
                // Nothing to do explicitly here if we bind in Render()
            }
        }

        public void Update(float deltaTime, Vector3 cameraPosition) {
            if (!_initialized) return;

            // Calculate current chunk
            var chunkX = (int)Math.Floor(cameraPosition.X / _chunkSizeInUnits);
            var chunkY = (int)Math.Floor(cameraPosition.Y / _chunkSizeInUnits);

            // Queue new chunks
            for (int x = chunkX - RenderDistance; x <= chunkX + RenderDistance; x++) {
                for (int y = chunkY - RenderDistance; y <= chunkY + RenderDistance; y++) {
                    if (x < 0 || y < 0) continue;

                    var uX = (uint)x;
                    var uY = (uint)y;

                    var chunkId = (ulong)uX << 32 | uY;
                    if (!_chunks.ContainsKey(chunkId)) {
                        var chunk = new TerrainChunk(uX, uY);
                        if (_chunks.TryAdd(chunkId, chunk)) {
                            _generationQueue.Enqueue(chunk);
                        }
                    }
                }
            }

            // Process Generation Queue (Background)
            int maxConcurrency = 2;
            while (_activeGenerations < maxConcurrency && !_generationQueue.IsEmpty) {
                System.Threading.Interlocked.Increment(ref _activeGenerations);
                Task.Run(ProcessGenerationQueueItem);
            }
        }

        private async Task ProcessGenerationQueueItem() {
            try {
                while (_generationQueue.TryDequeue(out var chunk)) {
                    GenerateChunk(chunk);
                }
            }
            finally {
                System.Threading.Interlocked.Decrement(ref _activeGenerations);
            }
        }

        public void ProcessUploads(float timeBudgetMs) {
            if (!_initialized) return;

            var sw = Stopwatch.StartNew();

            // Prioritize partial updates for responsiveness
            ProcessPartialUpdates(sw, timeBudgetMs);

            while (_uploadQueue.TryPeek(out var chunk)) {
                if (sw.Elapsed.TotalMilliseconds > timeBudgetMs) {
                    break;
                }

                if (_uploadQueue.TryDequeue(out chunk)) {
                    UploadChunk(chunk);
                }
            }
        }

        private unsafe void ProcessPartialUpdates(Stopwatch sw, float timeBudgetMs) {
            // Re-queue chunks that we don't finish
            int processedCount = 0;
            int initialCount = _partialUpdateQueue.Count;

            while (processedCount < initialCount && sw.Elapsed.TotalMilliseconds < timeBudgetMs) {
                if (_partialUpdateQueue.TryDequeue(out var chunk)) {
                    _queuedForPartialUpdate.TryRemove(chunk, out _);

                    if (chunk.HasDirtyBlocks()) {
                        UpdateChunk(chunk);
                    }
                    processedCount++;
                }
                else {
                    break;
                }
            }
        }

        private unsafe void UpdateChunk(TerrainChunk chunk) {
            if (!chunk.IsGenerated || chunk.VAO == 0) return;

            // Temporary buffers for single landblock
            Span<VertexLandscape> tempVertices = stackalloc VertexLandscape[TerrainGeometryGenerator.VerticesPerLandblock];
            Span<uint> tempIndices = stackalloc uint[TerrainGeometryGenerator.IndicesPerLandblock]; // Unused but required by signature

            _gl.BindVertexArray(chunk.VAO);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, chunk.VBO);

            int updates = 0;
            bool boundsChanged = false;
            while (chunk.TryGetNextDirty(out int lx, out int ly)) {
                int vertexOffset = chunk.LandblockVertexOffsets[ly * 8 + lx];
                if (vertexOffset == -1) continue; // No geometry for this block

                var landblockX = chunk.LandblockStartX + (uint)lx;
                var landblockY = chunk.LandblockStartY + (uint)ly;

                if (_landscapeDoc.Region is null) continue;

                var landblockID = _landscapeDoc.Region.GetLandblockId((int)landblockX, (int)landblockY);

                // _log.LogTrace("Updating landblock {LBX},{LBY} (ID: {LBID:X8}) in chunk {CX},{CY}", landblockX, landblockY, landblockID, chunk.ChunkX, chunk.ChunkY);

                // Generate geometry for this single landblock
                // We pass 0 as currentVertexIndex/currentIndexPosition because we only care about the vertex data relative to itself
                // The indices generated will be wrong (relative to 0), but we don't upload them.
                var (lbMinZ, lbMaxZ) = TerrainGeometryGenerator.GenerateLandblockGeometry(
                    landblockX, landblockY, landblockID,
                    _landscapeDoc.Region, _surfaceManager!,
                    _landscapeDoc.TerrainCache.AsSpan(),
                    0, 0,
                    tempVertices, tempIndices
                );

                chunk.LandblockBoundsMinZ[ly * 8 + lx] = lbMinZ;
                chunk.LandblockBoundsMaxZ[ly * 8 + lx] = lbMaxZ;
                boundsChanged = true;

                // Upload vertices
                fixed (VertexLandscape* vPtr = tempVertices) {
                    _gl.BufferSubData(BufferTargetARB.ArrayBuffer, (nint)(vertexOffset * VertexLandscape.Size), (nuint)(tempVertices.Length * VertexLandscape.Size), vPtr);
                }
                updates++;
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
                chunk.Bounds = new BoundingBox(
                    new Vector3(chunk.ChunkX * 8 * 192f, chunk.ChunkY * 8 * 192f, minZ),
                    new Vector3((chunk.ChunkX + 1) * 8 * 192f, (chunk.ChunkY + 1) * 8 * 192f, maxZ)
                );
            }

            _gl.BindVertexArray(0);

            if (updates > 0) _log.LogTrace("Updated {Count} landblocks in chunk {CX},{CY}", updates, chunk.ChunkX, chunk.ChunkY);
        }

        private void GenerateChunk(TerrainChunk chunk) {
            try {
                // 8x8 landblocks per chunk -> 64 cells per landblock -> 4 vertices per cell
                // Total vertices = 8 * 8 * 64 * 4 = 16384 vertices max
                // Indices = 8 * 8 * 64 * 6 = 24576 indices max

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

            // Calculate camera distance to ground/target for line width scaling
            // For now, use the camera's Z height abot 0 plane as a rough approximation if looking down
            // Or just distance to origin? The shader uses it for pixel size approx at center.
            // Let's use distance from camera to the point (Camera.X, Camera.Y, 0)
            float camDist = Math.Abs(camera.Position.Z);
            // If camera is pitched, this might differ. 
            // Better: use the distance to the "look at" point or just length of position if looking at origin?
            // "uCameraDistance" in shader calculates "worldUnitsPerPixel". 
            // If we are looking at the terrain, the distance to the terrain surface is what matters.
            // Let's try using the Z height for top-down, or distance for perspective.
            _shader.SetUniform("uCameraDistance", camDist < 1f ? 1f : camDist);


            if (ShowBrush) {
                // _log.LogTrace("Render Brush: Pos={Pos} Rad={Rad} Show={Show}", BrushPosition, BrushRadius, ShowBrush);
            }

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
            var chunkId = (ulong)chunkX << 32 | chunkY;

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
        }
    }
}