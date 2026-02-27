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
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Lib;
using WorldBuilder.Shared.Services;

namespace Chorizite.OpenGLSDLBackend.Lib {
    public class TerrainRenderManager : IDisposable {
        private readonly GL _gl;
        private readonly ILogger _log;
        private readonly LandscapeDocument _landscapeDoc;
        private readonly IDocumentManager _documentManager;
        private readonly ConcurrentDictionary<ushort, TerrainChunk> _chunks = new();
        private readonly CancellationTokenSource _cts = new();

        // Job queues
        private readonly ConcurrentDictionary<ushort, TerrainChunk> _pendingGeneration = new();
        private readonly ConcurrentQueue<TerrainChunk> _uploadQueue = new();
        private readonly ConcurrentQueue<TerrainChunk> _partialUpdateQueue = new();
        private readonly ConcurrentQueue<TerrainChunk> _readyForUploadQueue = new();
        private readonly ConcurrentDictionary<TerrainChunk, byte> _queuedForPartialUpdate = new();
        private int _activeGenerations = 0;
        private int _activePartialUpdates = 0;

        // Distance-based unloading
        private const float UnloadDelay = 15f;
        private readonly ConcurrentDictionary<ushort, float> _outOfRangeTimers = new();

        // Constants
        private const int MaxVertices = 24576;
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
        public Vector4 BrushColor { get; set; } = LandscapeColorsSettings.Instance.Brush;
        public bool ShowBrush { get; set; }
        public int BrushShape { get; set; }

        public LandscapeDocument LandscapeDocument => _landscapeDoc;

        // Grid settings
        public bool ShowLandblockGrid { get; set; }
        public bool ShowCellGrid { get; set; }
        public Vector3 LandblockGridColor { get; set; }
        public Vector3 CellGridColor { get; set; }
        public float GridLineWidth { get; set; } = 1.0f;
        public float GridOpacity { get; set; } = 1.0f;
        public float ScreenHeight { get; set; } = 1080.0f;
        public bool ShowUnwalkableSlopes { get; set; }
        public float LightIntensity { get; set; } = 1.0f;
        public float TimeOfDay { get; set; } = 0.5f;
        public Vector3 SunlightColor { get; set; } = Vector3.One;
        public Vector3 AmbientColor { get; set; } = new Vector3(0.4f, 0.4f, 0.4f);
        public Vector3 LightDirection { get; set; } = Vector3.Normalize(new Vector3(1.2f, 0.0f, 0.5f));

        private readonly Frustum _frustum;
        private readonly IDatReaderWriter _dats;
        private readonly OpenGLGraphicsDevice _graphicsDevice;
        private LandSurfaceManager? _surfaceManager;
        private bool _ownsSurfaceManager;

        public static uint CurrentVAO;

        public TerrainRenderManager(GL gl, ILogger log, LandscapeDocument landscapeDoc, IDatReaderWriter dats,
            OpenGLGraphicsDevice graphicsDevice, IDocumentManager documentManager, Frustum frustum, LandSurfaceManager? surfaceManager = null) {
            _gl = gl;
            _log = log;
            _landscapeDoc = landscapeDoc;
            _dats = dats;
            _graphicsDevice = graphicsDevice;
            _documentManager = documentManager;
            _frustum = frustum;
            _surfaceManager = surfaceManager;
            _ownsSurfaceManager = surfaceManager == null;
            log.LogTrace($"Initialized TerrainRenderManager");

            _landscapeDoc.LandblockChanged += OnLandblockChanged;
        }

        public float GetHeight(float x, float y) {
            if (_landscapeDoc?.Region is not RegionInfo regionInfo) return 0;

            // Convert to map coordinates (0,0 at top-left of map)
            float mapX = x - regionInfo.MapOffset.X;
            float mapY = y - regionInfo.MapOffset.Y;

            if (mapX < 0 || mapY < 0) return 0; // Out of bounds

            int lbX = (int)(mapX / regionInfo.LandblockSizeInUnits);
            int lbY = (int)(mapY / regionInfo.LandblockSizeInUnits);

            if (lbX >= regionInfo.MapWidthInLandblocks || lbY >= regionInfo.MapHeightInLandblocks) return 0;

            uint chunkX = (uint)lbX / 8;
            uint chunkY = (uint)lbY / 8;
            ushort chunkId = (ushort)((chunkX << 8) | chunkY);

            if (!_landscapeDoc.LoadedChunks.TryGetValue(chunkId, out var chunk)) return 0; // Chunk not loaded

            // Get 9x9 entries for the landblock
            var entries = new TerrainEntry[81];
            int localLbX = lbX % 8;
            int localLbY = lbY % 8;

            int startX = localLbX * 8;
            int startY = localLbY * 8;

            for (int dy = 0; dy < 9; dy++) {
                for (int dx = 0; dx < 9; dx++) {
                    int srcIdx = (startY + dy) * 65 + (startX + dx);
                    int dstIdx = dx * 9 + dy;
                    if (srcIdx < chunk.MergedEntries.Length) {
                        entries[dstIdx] = chunk.MergedEntries[srcIdx];
                    }
                }
            }

            // Local position within landblock
            Vector3 localPos = new Vector3(
                mapX - (lbX * regionInfo.LandblockSizeInUnits),
                mapY - (lbY * regionInfo.LandblockSizeInUnits),
                0
            );

            return TerrainUtils.GetHeight(regionInfo.Region, entries, (uint)lbX, (uint)lbY, localPos);
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
            if (_landscapeDoc.Region is ITerrainInfo regionInfo) {
                if (_surfaceManager == null) {
                    _surfaceManager = new LandSurfaceManager(_graphicsDevice, _dats, regionInfo.Region, _log);
                    _ownsSurfaceManager = true;
                }
                _chunkSizeInUnits = regionInfo.LandblockSizeInUnits * LandblocksPerChunk;
            }
        }

        public void Update(float deltaTime, ICamera camera) {
            if (!_initialized) return;

            if (_landscapeDoc.Region is null) return;

            // Calculate current chunk
            var pos = new Vector2(camera.Position.X, camera.Position.Y) - _landscapeDoc.Region.MapOffset;
            var chunkX = (int)Math.Floor(pos.X / _chunkSizeInUnits);
            var chunkY = (int)Math.Floor(pos.Y / _chunkSizeInUnits);

            // Queue new chunks
            for (int x = chunkX - RenderDistance; x <= chunkX + RenderDistance; x++) {
                for (int y = chunkY - RenderDistance; y <= chunkY + RenderDistance; y++) {
                    if (x < 0 || y < 0) continue;

                    var uX = (uint)x;
                    var uY = (uint)y;

                    var chunkId = (ushort)((uX << 8) | uY);
                    if (!_chunks.ContainsKey(chunkId)) {
                        var chunk = new TerrainChunk(_gl, uX, uY);
                        if (_chunks.TryAdd(chunkId, chunk)) {
                            _pendingGeneration[chunkId] = chunk;
                            _log.LogInformation("Queued terrain chunk ({X},{Y}) for generation | Total pending: {Pending}", x, y, _pendingGeneration.Count);
                        }
                    }
                }
            }

            // Clean up chunks that are out of range (with delay)
            // Note: We only unload based on distance, not frustum. This ensures chunks stay cached
            // once loaded, so panning the camera doesn't cause constant reloads.
            var chunkKeysToRemove = new List<ushort>();
            foreach (var (key, chunk) in _chunks) {
                int dx = (int)chunk.ChunkX - chunkX;
                int dy = (int)chunk.ChunkY - chunkY;
                if (Math.Abs(dx) > RenderDistance || Math.Abs(dy) > RenderDistance) {
                    var elapsed = _outOfRangeTimers.AddOrUpdate(key, deltaTime, (_, e) => e + deltaTime);
                    if (elapsed >= UnloadDelay) {
                        chunkKeysToRemove.Add(key);
                    }
                }
                else {
                    _outOfRangeTimers.TryRemove(key, out _);
                }
            }

            foreach (var key in chunkKeysToRemove) {
                _log.LogInformation("Unloading terrain chunk ({X},{Y}) - out of range", _chunks[key].ChunkX, _chunks[key].ChunkY);
                if (_chunks.TryRemove(key, out var chunk)) {
                    _pendingGeneration.TryRemove(key, out _);
                    chunk.Dispose();
                }
                _outOfRangeTimers.TryRemove(key, out _);
            }

            while (_activeGenerations < 12 && !_pendingGeneration.IsEmpty) {
                // Pick the nearest pending chunk (Euclidean distance with view direction bias)
                TerrainChunk? nearest = null;
                float bestPriority = float.MaxValue;
                ushort bestKey = 0;

                Vector3 camDir3 = camera.Forward;
                Vector2 camDir2D = new Vector2(camDir3.X, camDir3.Y);
                if (camDir2D.LengthSquared() > 0.001f) {
                    camDir2D = Vector2.Normalize(camDir2D);
                }
                else {
                    camDir2D = Vector2.Zero;
                }

                foreach (var (key, chunk) in _pendingGeneration) {
                    float dx = (int)chunk.ChunkX - chunkX;
                    float dy = (int)chunk.ChunkY - chunkY;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);

                    float priority = dist;
                    if (dist > 0.1f && camDir2D != Vector2.Zero) {
                        Vector2 dirToChunk = Vector2.Normalize(new Vector2(dx, dy));
                        float dot = Vector2.Dot(camDir2D, dirToChunk);
                        priority -= dot * 1.5f;
                    }

                    if (priority < bestPriority) {
                        bestPriority = priority;
                        nearest = chunk;
                        bestKey = key;
                    }
                }

                if (nearest == null || !_pendingGeneration.TryRemove(bestKey, out var chunkToGenerate))
                    break;

                int chosenDist = Math.Max(Math.Abs((int)chunkToGenerate.ChunkX - chunkX), Math.Abs((int)chunkToGenerate.ChunkY - chunkY));

                // Skip if now out of range (don't skip based on frustum - that causes flickering when camera pans)
                if (chosenDist > RenderDistance) {
                    if (_chunks.TryRemove(bestKey, out _)) {
                        chunkToGenerate.Dispose();
                    }
                    continue;
                }

                System.Threading.Interlocked.Increment(ref _activeGenerations);
                Task.Run(async () => {
                    try {
                        await GenerateChunk(chunkToGenerate, _cts.Token);
                    }
                    finally {
                        System.Threading.Interlocked.Decrement(ref _activeGenerations);
                    }
                }, _cts.Token);
            }
        }

        private FrustumTestResult IsChunkInFrustum(int chunkX, int chunkY) {
            var offset = _landscapeDoc.Region?.MapOffset ?? Vector2.Zero;
            var minX = chunkX * _chunkSizeInUnits + offset.X;
            var minY = chunkY * _chunkSizeInUnits + offset.Y;
            var maxX = (chunkX + 1) * _chunkSizeInUnits + offset.X;
            var maxY = (chunkY + 1) * _chunkSizeInUnits + offset.Y;

            var box = new BoundingBox(
                new Vector3(minX, minY, -1000f),
                new Vector3(maxX, maxY, 5000f)
            );
            return _frustum.TestBox(box);
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
                    // Skip if this chunk is no longer in render distance (don't skip based on frustum)
                    if (!IsWithinRenderDistance(chunk, (int)chunk.ChunkX, (int)chunk.ChunkY)) {
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
                }, _cts.Token);
            }
        }

        private void ProcessChunkUpdate(TerrainChunk chunk) {
            try {
                // Temporary buffers for single landblock
                var tempVertices = new VertexLandscape[TerrainGeometryGenerator.VerticesPerLandblock];
                var tempIndices = new uint[TerrainGeometryGenerator.IndicesPerLandblock]; // Unused but required by signature

                while (chunk.TryGetNextDirty(out int lx, out int ly)) {
                    if (_cts.Token.IsCancellationRequested) return;

                    int vertexOffset = chunk.LandblockVertexOffsets[ly * 8 + lx];
                    if (vertexOffset == -1) continue; // No geometry for this block

                    var landblockX = chunk.LandblockStartX + (uint)lx;
                    var landblockY = chunk.LandblockStartY + (uint)ly;

                    if (_landscapeDoc.Region is null) continue;

                    var landblockID = _landscapeDoc.Region.GetLandblockId((int)landblockX, (int)landblockY);

                    if (!_landscapeDoc.LoadedChunks.TryGetValue(chunk.GetChunkId(), out var landscapeChunk)) continue;

                    var (lbMinZ, lbMaxZ) = TerrainGeometryGenerator.GenerateLandblockGeometry(
                        landblockX, landblockY, landblockID,
                        _landscapeDoc.Region, _surfaceManager!,
                        landscapeChunk.MergedEntries.AsSpan(),
                        0, 0,
                        tempVertices, tempIndices,
                        chunk.LandblockStartX, chunk.LandblockStartY
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

        private async Task GenerateChunk(TerrainChunk chunk, CancellationToken ct) {
            try {
                _log.LogInformation("Starting terrain chunk generation for ({X},{Y})", chunk.ChunkX, chunk.ChunkY);
                
                var landscapeChunk = await _landscapeDoc!.GetOrLoadChunkAsync(chunk.GetChunkId(), _dats!, _documentManager, ct);

                if (ct.IsCancellationRequested) return;

                var vertices = new VertexLandscape[MaxVertices];
                var indices = new uint[MaxIndices];
                int vCount = 0;
                int iCount = 0;

                if (_landscapeDoc?.Region != null) {
                    TerrainGeometryGenerator.GenerateChunkGeometry(
                        chunk,
                        _landscapeDoc.Region,
                        _surfaceManager!,
                        landscapeChunk.MergedEntries,
                        vertices,
                        indices,
                        out vCount,
                        out iCount
                    );
                }
                else {
                    _log.LogWarning("Cannot generate chunk {CX},{CY}: Region is null", chunk.ChunkX, chunk.ChunkY);
                }

                if (ct.IsCancellationRequested) return;

                if (vCount > 0) {
                    chunk.GeneratedVertices = vertices.AsMemory(0, vCount);
                    chunk.GeneratedIndices = indices.AsMemory(0, iCount);
                }

                _uploadQueue.Enqueue(chunk);
                _log.LogInformation("Completed terrain chunk generation for ({X},{Y}) | Vertices: {V} | Queued for upload", chunk.ChunkX, chunk.ChunkY, vCount);
            }
            catch (OperationCanceledException) {
                _log.LogInformation("Terrain chunk generation cancelled for ({X},{Y})", chunk.ChunkX, chunk.ChunkY);
                // Ignore
            }
            catch (Exception ex) {
                _log.LogError(ex, "Error generating chunk {CX},{CY}", chunk.ChunkX, chunk.ChunkY);
            }
        }

        private unsafe void UploadChunk(TerrainChunk chunk) {
            _log.LogInformation("Uploading terrain chunk ({X},{Y})", chunk.ChunkX, chunk.ChunkY);
            
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
            GpuMemoryTracker.TrackAllocation(vertices.Length * VertexLandscape.Size, GpuResourceType.Buffer);

            chunk.EBO = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, chunk.EBO);

            fixed (uint* iPtr = indices) {
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), iPtr,
                    BufferUsageARB.StaticDraw);
            }
            GpuMemoryTracker.TrackAllocation(indices.Length * sizeof(uint), GpuResourceType.Buffer);

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
            chunk.VertexCount = vertices.Length;
            chunk.IsGenerated = true;

            // Clear cpu memory
            chunk.GeneratedVertices = Memory<VertexLandscape>.Empty;
            chunk.GeneratedIndices = Memory<uint>.Empty;

            _gl.BindVertexArray(0);
        }

        public unsafe void Render(Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix, Matrix4x4 viewProjectionMatrix, Vector3 cameraPosition, float fieldOfView) {
            if (!_initialized || _shader is null || (_shader is GLSLShader glsl && glsl.Program == 0)) return;

            CurrentVAO = 0;

            _shader.Bind();

            // Set uniforms
            _shader.SetUniform("xView", viewMatrix);
            _shader.SetUniform("xProjection", projectionMatrix);
            _shader.SetUniform("xWorld", Matrix4x4.Identity); // Chunks are already in world space coordinates
            _shader.SetUniform("uAlpha", 1.0f);
            var region = _landscapeDoc.Region;
            if (region != null) {
                region.TimeOfDay = TimeOfDay;
            }
            _shader.SetUniform("uSunlightColor", region?.SunlightColor ?? SunlightColor);
            _shader.SetUniform("uAmbientColor", (region?.AmbientColor ?? AmbientColor) * LightIntensity);
            _shader.SetUniform("uLightDirection", region?.LightDirection ?? LightDirection);

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
            _shader.SetUniform("uGridOffset", _landscapeDoc.Region?.MapOffset ?? Vector2.Zero);
            _shader.SetUniform("uShowUnwalkableSlopes", ShowUnwalkableSlopes ? 1 : 0);
            _shader.SetUniform("uFloorZ", TerrainUtils.FloorZ);

            float camDist = Math.Abs(cameraPosition.Z);
            _shader.SetUniform("uCameraDistance", camDist < 1f ? 1f : camDist);
            _shader.SetUniform("uCameraFov", fieldOfView);

            if (_surfaceManager != null) {
                _surfaceManager.TerrainAtlas.Bind(0);
                _shader.SetUniform("xOverlays", 0);

                _surfaceManager.AlphaAtlas.Bind(1);
                _shader.SetUniform("xAlphas", 1);
            }

            CurrentVAO = 0;
            foreach (var chunk in _chunks.Values) {
                if (!chunk.IsGenerated || chunk.IndexCount == 0) continue;

                if (_frustum.TestBox(chunk.Bounds) == FrustumTestResult.Outside) continue;

                if (CurrentVAO != chunk.VAO) {
                    _gl.BindVertexArray(chunk.VAO);
                    CurrentVAO = chunk.VAO;
                }
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
            _cts.Cancel();
            _cts.Dispose();
            _landscapeDoc.LandblockChanged -= OnLandblockChanged;
            foreach (var chunk in _chunks.Values) {
                chunk.Dispose();
            }

            _chunks.Clear();
            _pendingGeneration.Clear();
            _outOfRangeTimers.Clear();
            if (_ownsSurfaceManager) {
                _surfaceManager?.Dispose();
            }
        }
    }
}