using Chorizite.Core.Render;
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

namespace Chorizite.OpenGLSDLBackend.Lib {
    public class TerrainRenderManager : IDisposable {
        private readonly GL _gl;
        private readonly ILogger _log;
        private readonly LandscapeDocument _landscapeDoc;
        private readonly ConcurrentDictionary<ulong, TerrainChunk> _chunks = new();

        // Job queues
        private readonly ConcurrentQueue<TerrainChunk> _generationQueue = new();
        private readonly ConcurrentQueue<TerrainChunk> _uploadQueue = new();
        private int _activeGenerations = 0;

        // Render state
        private IShader _shader;
        private bool _initialized;

        // Statistics
        public int RenderDistance { get; set; } = 8;
        public int QueuedUploads => _uploadQueue.Count;
        public int QueuedGenerations => _generationQueue.Count;
        public int ActiveChunks => _chunks.Count;

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
            log.LogInformation($"Initialized TerrainRenderManager");
        }

        public void Initialize(IShader shader) {
            _shader = shader;
            _initialized = true;

            // Initialize Surface Manager
            if (_landscapeDoc.Region != null) {
                _surfaceManager = new LandSurfaceManager(_graphicsDevice, _dats, _landscapeDoc.Region._region, _log);
            }

            // Bind textures
            if (_surfaceManager != null) {
                // Nothing to do explicitly here if we bind in Render()
            }
        }

        public void Update(float deltaTime, Vector3 cameraPosition) {
            if (!_initialized) return;

            // Calculate current chunk
            var chunkX = (int)Math.Floor(cameraPosition.X / (192f * 8f));
            var chunkY = (int)Math.Floor(cameraPosition.Y / (192f * 8f));

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

            while (_uploadQueue.TryPeek(out var chunk)) {
                if (sw.Elapsed.TotalMilliseconds > timeBudgetMs) {
                    break;
                }

                if (_uploadQueue.TryDequeue(out chunk)) {
                    UploadChunk(chunk);
                }
            }
        }

        private void GenerateChunk(TerrainChunk chunk) {
            try {
                // 8x8 landblocks per chunk -> 64 cells per landblock -> 4 vertices per cell
                // Total vertices = 8 * 8 * 64 * 4 = 16384 vertices max
                // Indices = 8 * 8 * 64 * 6 = 24576 indices max

                var vertices = new VertexLandscape[16384];
                var indices = new uint[24576];
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
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (uint)VertexLandscape.Size, (void*)0);

            // 1: Normal (3 float)
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, (uint)VertexLandscape.Size, (void*)12);

            // 2: TexCoord0 (4 byte)
            _gl.EnableVertexAttribArray(2);
            _gl.VertexAttribIPointer(2, 4, VertexAttribIType.UnsignedByte, (uint)VertexLandscape.Size, (void*)24);

            // 3: PackedOverlay0 (4 byte)
            _gl.EnableVertexAttribArray(3);
            _gl.VertexAttribIPointer(3, 4, VertexAttribIType.UnsignedByte, (uint)VertexLandscape.Size, (void*)28);

            // 4: PackedOverlay1 (4 byte)
            _gl.EnableVertexAttribArray(4);
            _gl.VertexAttribIPointer(4, 4, VertexAttribIType.UnsignedByte, (uint)VertexLandscape.Size, (void*)32);

            // 5: PackedOverlay2 (4 byte)
            _gl.EnableVertexAttribArray(5);
            _gl.VertexAttribIPointer(5, 4, VertexAttribIType.UnsignedByte, (uint)VertexLandscape.Size, (void*)36);

            // 6: PackedRoad0 (4 byte)
            _gl.EnableVertexAttribArray(6);
            _gl.VertexAttribIPointer(6, 4, VertexAttribIType.UnsignedByte, (uint)VertexLandscape.Size, (void*)40);

            // 7: PackedRoad1 (4 byte)
            _gl.EnableVertexAttribArray(7);
            _gl.VertexAttribIPointer(7, 4, VertexAttribIType.UnsignedByte, (uint)VertexLandscape.Size, (void*)44);

            chunk.IndexCount = indices.Length;
            chunk.IsGenerated = true;

            // Clear cpu memory
            chunk.GeneratedVertices = Memory<VertexLandscape>.Empty;
            chunk.GeneratedIndices = Memory<uint>.Empty;

            _gl.BindVertexArray(0);
        }

        public unsafe void Render(ICamera camera) {
            if (!_initialized) return;

            _shader.Bind();

            // Set uniforms
            _shader.SetUniform("xView", camera.ViewMatrix);
            _shader.SetUniform("xProjection", camera.ProjectionMatrix);
            _shader.SetUniform("xWorld", Matrix4x4.Identity); // Chunks are already in world space coordinates
            _shader.SetUniform("uAlpha", 1.0f);
            _shader.SetUniform("xAmbient", 0.5f); // 0.5 ambient

            if (_surfaceManager != null) {
                _surfaceManager.TerrainAtlas.Bind(0);
                _shader.SetUniform("xOverlays", 0);

                _surfaceManager.AlphaAtlas.Bind(1);
                _shader.SetUniform("xAlphas", 1);
            }

            foreach (var chunk in _chunks.Values) {
                if (!chunk.IsGenerated || chunk.IndexCount == 0) continue;

                // TODO: Frustum Culling

                _gl.BindVertexArray(chunk.VAO);
                _gl.DrawElements(PrimitiveType.Triangles, (uint)chunk.IndexCount, DrawElementsType.UnsignedInt,
                    (void*)0);
            }

            _gl.BindVertexArray(0);
        }

        public void Dispose() {
            foreach (var chunk in _chunks.Values) {
                chunk.Dispose();
            }

            _chunks.Clear();
        }
    }
}