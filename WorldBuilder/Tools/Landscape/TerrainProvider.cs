using Chorizite.Core.Dats;
using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using Chorizite.Core.Render.Vertex;
using Chorizite.OpenGLSDLBackend;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Tools.Landscape {
    public partial class TerrainProvider {
        /// <summary>
        /// The size of the map (number of landblocks along each edge).
        /// </summary>
        public static readonly uint MapSize = 254;

        /// <summary>
        /// The unit length of a landblock.
        /// </summary>
        public static readonly uint LandblockLength = 192;

        /// <summary>
        /// The number of cells along an edge of a landblock.
        /// </summary>
        public static readonly uint LandblockEdgeCellCount = 8;

        /// <summary>
        /// Cell size within a landblock.
        /// </summary>
        public static readonly float CellSize = LandblockLength / (float)LandblockEdgeCellCount; // 24.0f

        /// <summary>
        /// Each cell has 4 vertices (no sharing between cells due to per-cell texture/UV variations).
        /// </summary>
        public static readonly int VerticesPerCell = 4;

        /// <summary>
        /// Each cell has 6 indices (2 triangles).
        /// </summary>
        public static readonly int IndicesPerCell = 6;

        // Chunk configuration
        private readonly uint _chunkSizeInLandblocks;

        // Pre-calculated totals per landblock
        private static readonly int CellsPerLandblock = (int)(LandblockEdgeCellCount * LandblockEdgeCellCount);
        private static readonly int VerticesPerLandblock = CellsPerLandblock * VerticesPerCell;
        private static readonly int IndicesPerLandblock = CellsPerLandblock * IndicesPerCell;

        // Dynamic totals per chunk (based on chunk size)
        private readonly int _cellsPerChunk;
        private readonly int _verticesPerChunk;
        private readonly int _indicesPerChunk;
        private readonly uint _chunkWorldSize; // Size of chunk in world units

        public readonly OpenGLRenderer _renderer;
        public readonly IDatReaderWriter _dats;
        public readonly TerrainDocument _terrain;
        public readonly Region _region;
        public readonly LandSurfaceManager LandSurf;

        // Chunk management
        private readonly Dictionary<ulong, TerrainChunk> _chunks = new();
        private readonly Dictionary<ulong, ChunkInfo> _chunkInfo = new();
        
        public struct ChunkInfo {
            public uint ActualLandblockCountX;
            public uint ActualLandblockCountY;
            public int ActualVertexCount;
            public int ActualIndexCount;
        }

        /// <summary>
        /// Gets the chunk size in landblocks.
        /// </summary>
        public uint ChunkSizeInLandblocks => _chunkSizeInLandblocks;

        public TerrainProvider(OpenGLRenderer renderer, TerrainDocument terrain, IDatReaderWriter dats, uint chunkSizeInLandblocks = 16) {
            _renderer = renderer;
            _dats = dats;
            _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            _chunkSizeInLandblocks = Math.Max(1, chunkSizeInLandblocks);

            // Calculate chunk-dependent values
            _cellsPerChunk = (int)(_chunkSizeInLandblocks * _chunkSizeInLandblocks * LandblockEdgeCellCount * LandblockEdgeCellCount);
            _verticesPerChunk = _cellsPerChunk * VerticesPerCell;
            _indicesPerChunk = _cellsPerChunk * IndicesPerCell;
            _chunkWorldSize = _chunkSizeInLandblocks * LandblockLength;

            
            if (!_dats.TryGet<Region>(0x13000000, out var region)) {
                throw new Exception("Failed to load region");
            }
            _region = region;

            LandSurf = new LandSurfaceManager(_renderer, _dats, _region);
        }

        public void UpdateChunks(Vector3 cameraPosition, Matrix4x4 viewProjectionMatrix) {
            var frustum = new Frustum(viewProjectionMatrix);
            var chunksToGenerate = GetChunksToGenerate(cameraPosition);
            bool didGenerate = false;

            // Generate new chunks
            foreach (var chunkCoord in chunksToGenerate) {
                if (!_chunks.ContainsKey(chunkCoord)) {
                    GenerateChunk(chunkCoord);
                    didGenerate = true;
                }
            }

            foreach (var kvp in _chunks) {
                var chunk = kvp.Value;

                // Update visibility based on frustum culling
                chunk.IsVisible = frustum.IntersectsBoundingBox(chunk.BoundingBox);

                // Create graphics resources if visible and not already created
                if (chunk.IsVisible && chunk.VertexBuffer == null && chunk.IsGenerated) {
                    CreateChunkGraphicsResources(chunk);
                }
            }
        }

        private List<ulong> GetChunksToGenerate(Vector3 cameraPosition) {
            var chunksToGenerate = new List<ulong>();

            // Calculate which chunk the camera is in (in chunk coordinates, not landblock coordinates)
            var chunkX = (uint)Math.Max(0, Math.Min((MapSize / _chunkSizeInLandblocks) - 1, cameraPosition.X / _chunkWorldSize));
            var chunkY = (uint)Math.Max(0, Math.Min((MapSize / _chunkSizeInLandblocks) - 1, cameraPosition.Y / _chunkWorldSize));

            // Determine how many chunks to load around the camera
            var chunkRange = Math.Max(1u, MapSize / _chunkSizeInLandblocks);

            var maxChunksX = (MapSize + _chunkSizeInLandblocks - 1) / _chunkSizeInLandblocks; // Ceiling division
            var maxChunksY = (MapSize + _chunkSizeInLandblocks - 1) / _chunkSizeInLandblocks;

            var minX = (uint)Math.Max(0, (int)chunkX - chunkRange);
            var maxX = (uint)Math.Min(maxChunksX - 1, chunkX + chunkRange);
            var minY = (uint)Math.Max(0, (int)chunkY - chunkRange);
            var maxY = (uint)Math.Min(maxChunksY - 1, chunkY + chunkRange);

            for (uint y = minY; y <= maxY; y++) {
                for (uint x = minX; x <= maxX; x++) {
                    var chunkId = GetChunkId(x, y);
                    chunksToGenerate.Add(chunkId);
                }
            }

            return chunksToGenerate;
        }

        private ulong GetChunkId(uint chunkX, uint chunkY) {
            return ((ulong)chunkX << 32) | chunkY;
        }

        private void GenerateChunk(ulong chunkId) {
            var chunkX = (uint)(chunkId >> 32);
            var chunkY = (uint)(chunkId & 0xFFFFFFFF);

            var chunk = new TerrainChunk {
                LandblockX = chunkX * _chunkSizeInLandblocks,
                LandblockY = chunkY * _chunkSizeInLandblocks,
                IsGenerated = false,
                IsVisible = false
            };

            // Calculate actual dimensions for this chunk
            var maxChunksX = (MapSize + _chunkSizeInLandblocks - 1) / _chunkSizeInLandblocks;
            var maxChunksY = (MapSize + _chunkSizeInLandblocks - 1) / _chunkSizeInLandblocks;

            var actualLandblockCountX = Math.Min(_chunkSizeInLandblocks, MapSize - chunk.LandblockX);
            var actualLandblockCountY = Math.Min(_chunkSizeInLandblocks, MapSize - chunk.LandblockY);

            // Store chunk info
            _chunkInfo[chunkId] = new ChunkInfo {
                ActualLandblockCountX = actualLandblockCountX,
                ActualLandblockCountY = actualLandblockCountY,
                ActualVertexCount = (int)(actualLandblockCountX * actualLandblockCountY * CellsPerLandblock * VerticesPerCell),
                ActualIndexCount = (int)(actualLandblockCountX * actualLandblockCountY * CellsPerLandblock * IndicesPerCell)
            };

            // Calculate bounding box...
            var minHeight = float.MaxValue;
            var maxHeight = float.MinValue;

            for (uint ly = 0; ly < actualLandblockCountY; ly++) {
                for (uint lx = 0; lx < actualLandblockCountX; lx++) {
                    var landblockX = chunk.LandblockX + lx;
                    var landblockY = chunk.LandblockY + ly;

                    if (landblockX >= MapSize || landblockY >= MapSize) continue;

                    var landblockID = (uint)((landblockX << 8) | landblockY);
                    var landblockData = _terrain.GetLandblock((ushort)landblockID);

                    if (landblockData != null) {
                        for (int i = 0; i < landblockData.Length; i++) {
                            var height = _region.LandDefs.LandHeightTable[landblockData[i].Height];
                            minHeight = Math.Min(minHeight, height);
                            maxHeight = Math.Max(maxHeight, height);
                        }
                    }
                }
            }

            chunk.BoundingBox = new BoundingBox(
                new Vector3(chunk.LandblockX * LandblockLength, chunk.LandblockY * LandblockLength, minHeight),
                new Vector3((chunk.LandblockX + actualLandblockCountX) * LandblockLength,
                           (chunk.LandblockY + actualLandblockCountY) * LandblockLength, maxHeight)
            );

            chunk.IsGenerated = true;
            _chunks[chunkId] = chunk;
        }

        private void CreateChunkGraphicsResources(TerrainChunk chunk) {
            if (chunk.VertexBuffer != null) return;

            var chunkX = chunk.LandblockX / _chunkSizeInLandblocks;
            var chunkY = chunk.LandblockY / _chunkSizeInLandblocks;
            var chunkId = GetChunkId(chunkX, chunkY);

            if (!_chunkInfo.TryGetValue(chunkId, out var chunkInfo)) {
                Console.WriteLine($"Warning: No chunk info found for chunk {chunkId}");
                return;
            }

            // Use actual vertex/index counts instead of maximum
            var vertices = new VertexLandscape[chunkInfo.ActualVertexCount];
            var indices = new uint[chunkInfo.ActualIndexCount];

            GenerateChunkGeometry(vertices, indices, chunkX, chunkY, out int actualVertexCount, out int actualIndexCount);

            if (actualVertexCount == 0 || actualIndexCount == 0) return;

            chunk.VertexBuffer = _renderer.GraphicsDevice.CreateVertexBuffer(
                VertexLandscape.Size * actualVertexCount,
                BufferUsage.Dynamic
            );
            chunk.VertexBuffer.SetData(vertices.AsSpan(0, actualVertexCount));

            chunk.IndexBuffer = _renderer.GraphicsDevice.CreateIndexBuffer(
                4 * actualIndexCount,
                BufferUsage.Dynamic
            );
            chunk.IndexBuffer.SetData(indices.AsSpan(0, actualIndexCount));

            chunk.VertexArray = _renderer.GraphicsDevice.CreateArrayBuffer(
                chunk.VertexBuffer,
                VertexLandscape.Format
            );

            chunk.VertexCount = actualVertexCount;
            chunk.IndexCount = actualIndexCount;
        }

        private void GenerateChunkGeometry(VertexLandscape[] vertices, uint[] indices, uint chunkX, uint chunkY, out int actualVertexCount, out int actualIndexCount) {
            uint currentVertexIndex = 0;
            uint currentIndexPosition = 0;

            var verticesSpan = vertices.AsSpan();
            var indicesSpan = indices.AsSpan();

            var chunkId = GetChunkId(chunkX, chunkY);
            if (!_chunkInfo.TryGetValue(chunkId, out var chunkInfo)) {
                actualVertexCount = 0;
                actualIndexCount = 0;
                return;
            }

            // Use actual dimensions instead of full chunk size
            for (uint ly = 0; ly < chunkInfo.ActualLandblockCountY; ly++) {
                for (uint lx = 0; lx < chunkInfo.ActualLandblockCountX; lx++) {
                    var landblockX = chunkX * _chunkSizeInLandblocks + lx;
                    var landblockY = chunkY * _chunkSizeInLandblocks + ly;

                    if (landblockX >= MapSize || landblockY >= MapSize) continue;

                    var landblockID = (uint)((landblockX << 8) | landblockY);
                    var landblockData = _terrain.GetLandblock((ushort)landblockID);

                    if (landblockData == null) {
                        currentVertexIndex += (uint)VerticesPerLandblock;
                        currentIndexPosition += (uint)IndicesPerLandblock;
                        continue;
                    }

                    float baseLandblockX = landblockX * LandblockLength;
                    float baseLandblockY = landblockY * LandblockLength;

                    for (uint cellY = 0; cellY < LandblockEdgeCellCount; cellY++) {
                        for (uint cellX = 0; cellX < LandblockEdgeCellCount; cellX++) {
                            GenerateCell(
                                baseLandblockX, baseLandblockY, cellX, cellY,
                                landblockData, landblockID,
                                ref currentVertexIndex, ref currentIndexPosition,
                                verticesSpan, indicesSpan
                            );
                        }
                    }
                }
            }

            actualVertexCount = (int)currentVertexIndex;
            actualIndexCount = (int)currentIndexPosition;
        }

        public Vector3[] GenerateCellVertices(float baseLandblockX, float baseLandblockY, uint cellX, uint cellY, TerrainEntry[] landblockData) {
            var vertices = new Vector3[4];

            // Get heights for the four corners
            var bottomLeft = GetTerrainEntryForCell(landblockData, cellX, cellY);        // SW
            var bottomRight = GetTerrainEntryForCell(landblockData, cellX + 1, cellY);   // SE
            var topRight = GetTerrainEntryForCell(landblockData, cellX + 1, cellY + 1);  // NE
            var topLeft = GetTerrainEntryForCell(landblockData, cellX, cellY + 1);       // NW

            // SW corner
            vertices[0] = new Vector3(
                baseLandblockX + (cellX * 24f),
                baseLandblockY + (cellY * 24f),
                _region.LandDefs.LandHeightTable[bottomLeft.Height]
            );

            // SE corner
            vertices[1] = new Vector3(
                baseLandblockX + ((cellX + 1) * 24f),
                baseLandblockY + (cellY * 24f),
                _region.LandDefs.LandHeightTable[bottomRight.Height]
            );

            // NE corner
            vertices[2] = new Vector3(
                baseLandblockX + ((cellX + 1) * 24f),
                baseLandblockY + ((cellY + 1) * 24f),
                _region.LandDefs.LandHeightTable[topRight.Height]
            );

            // NW corner
            vertices[3] = new Vector3(
                baseLandblockX + (cellX * 24f),
                baseLandblockY + ((cellY + 1) * 24f),
                _region.LandDefs.LandHeightTable[topLeft.Height]
            );

            return vertices;
        }

        public IEnumerable<TerrainChunk> GetVisibleChunks() {
            foreach (var chunk in _chunks.Values) {
                if (chunk.IsVisible && chunk.VertexBuffer != null) {
                    yield return chunk;
                }
            }
        }

        public int GetLoadedChunkCount() => _chunks.Count;
        public int GetVisibleChunkCount() => _chunks.Values.Count(c => c.IsVisible);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void GenerateCell(float baseLandblockX, float baseLandblockY, uint cellX, uint cellY,
                                  TerrainEntry[] landblockData, uint landblockID,
                                  ref uint currentVertexIndex, ref uint currentIndexPosition,
                                  Span<VertexLandscape> verticesSpan,
                                  Span<uint> indicesSpan) {

            // Get surface and rotation info
            uint surfNum = 0;
            var rotation = TextureMergeInfo.Rotation.Rot0;
            GetCellRotation(LandSurf, landblockID, landblockData, cellX, cellY, ref surfNum, ref rotation);
            var surfInfo = LandSurf.GetLandSurface(surfNum);

            // Get heights for the four corners
            var bottomLeft = GetTerrainEntryForCell(landblockData, cellX, cellY);        // SW
            var bottomRight = GetTerrainEntryForCell(landblockData, cellX + 1, cellY);   // SE
            var topRight = GetTerrainEntryForCell(landblockData, cellX + 1, cellY + 1);  // NE
            var topLeft = GetTerrainEntryForCell(landblockData, cellX, cellY + 1);       // NW

            ref VertexLandscape v0 = ref verticesSpan[(int)currentVertexIndex];     // SW
            ref VertexLandscape v1 = ref verticesSpan[(int)currentVertexIndex + 1]; // SE
            ref VertexLandscape v2 = ref verticesSpan[(int)currentVertexIndex + 2]; // NE
            ref VertexLandscape v3 = ref verticesSpan[(int)currentVertexIndex + 3]; // NW

            // Calculate split direction
            bool splitDiagonal = CalculateSplitDirection(landblockID >> 8, cellX, landblockID & 0xFF, cellY);

            LandSurf.FillVertexData(landblockID, cellX, cellY, baseLandblockX, baseLandblockY, ref v0, bottomLeft.Height, surfInfo, 0); // SW
            LandSurf.FillVertexData(landblockID, cellX + 1, cellY, baseLandblockX, baseLandblockY, ref v1, bottomRight.Height, surfInfo, 1); // SE
            LandSurf.FillVertexData(landblockID, cellX + 1, cellY + 1, baseLandblockX, baseLandblockY, ref v2, topRight.Height, surfInfo, 2); // NE
            LandSurf.FillVertexData(landblockID, cellX, cellY + 1, baseLandblockX, baseLandblockY, ref v3, topLeft.Height, surfInfo, 3); // NW

            // Calculate approximate normals (same for the cell)
            CalculateVertexNormals(ref v0, ref v1, ref v2, ref v3);

            // Generate indices with counter-clockwise winding
            ref uint indexRef = ref indicesSpan[(int)currentIndexPosition];

            if (!splitDiagonal) {
                // SW to NE split - reversed winding
                Unsafe.Add(ref indexRef, 0) = currentVertexIndex + 0; // SW
                Unsafe.Add(ref indexRef, 1) = currentVertexIndex + 3; // NW
                Unsafe.Add(ref indexRef, 2) = currentVertexIndex + 1; // SE
                Unsafe.Add(ref indexRef, 3) = currentVertexIndex + 1; // SE
                Unsafe.Add(ref indexRef, 4) = currentVertexIndex + 3; // NW
                Unsafe.Add(ref indexRef, 5) = currentVertexIndex + 2; // NE
            }
            else {
                // SE to NW split - reversed winding
                Unsafe.Add(ref indexRef, 0) = currentVertexIndex + 0; // SW
                Unsafe.Add(ref indexRef, 1) = currentVertexIndex + 2; // NE
                Unsafe.Add(ref indexRef, 2) = currentVertexIndex + 1; // SE
                Unsafe.Add(ref indexRef, 3) = currentVertexIndex + 0; // SW
                Unsafe.Add(ref indexRef, 4) = currentVertexIndex + 3; // NW
                Unsafe.Add(ref indexRef, 5) = currentVertexIndex + 2; // NE
            }

            currentVertexIndex += (uint)VerticesPerCell;
            currentIndexPosition += (uint)IndicesPerCell;
        }
        
        /// <summary>
        /// Gets a chunk by its ID
        /// </summary>
        public TerrainChunk GetChunk(ulong chunkId) {
            _chunks.TryGetValue(chunkId, out var chunk);
            return chunk;
        }

        /// <summary>
        /// Regenerates the graphics buffers for a specific chunk
        /// </summary>
        public void RegenerateChunkBuffers(TerrainChunk chunk) {
            if (chunk == null) return;

            // Dispose existing buffers
            chunk.VertexBuffer?.Dispose();
            chunk.IndexBuffer?.Dispose();
            chunk.VertexArray?.Dispose();

            // Clear buffer references
            chunk.VertexBuffer = null;
            chunk.IndexBuffer = null;
            chunk.VertexArray = null;

            // Regenerate graphics resources
            CreateChunkGraphicsResources(chunk);

            Console.WriteLine($"Regenerated buffers for chunk at ({chunk.LandblockX}, {chunk.LandblockY})");
        }

        private ulong GetChunkIdForLandblock(uint landblockX, uint landblockY) {
            var chunkX = landblockX / _chunkSizeInLandblocks;
            var chunkY = landblockY / _chunkSizeInLandblocks;
            return ((ulong)chunkX << 32) | chunkY;
        }

        /// <summary>
        /// Updates specific landblocks within a chunk without regenerating the entire chunk
        /// </summary>
        /// <param name="modifiedLandblocks">Dictionary of landblock coordinates to update</param>
        public void UpdateModifiedLandblocks(Dictionary<uint, Vector2> modifiedLandblocks) {
            var chunksToUpdate = new HashSet<ulong>();

            // Group landblocks by their chunks
            var landblocksByChunk = new Dictionary<ulong, List<uint>>();

            foreach (var kvp in modifiedLandblocks) {
                var landblockId = kvp.Key;
                var landblockX = landblockId >> 8;
                var landblockY = landblockId & 0xFF;

                var chunkId = GetChunkIdForLandblock(landblockX, landblockY);

                if (!landblocksByChunk.ContainsKey(chunkId)) {
                    landblocksByChunk[chunkId] = new List<uint>();
                }
                landblocksByChunk[chunkId].Add(landblockId);
            }

            // Update each affected chunk
            foreach (var kvp in landblocksByChunk) {
                var chunkId = kvp.Key;
                var landblockIds = kvp.Value;

                if (_chunks.TryGetValue(chunkId, out var chunk) && chunk.VertexBuffer != null) {
                    UpdateLandblocksInChunk(chunk, landblockIds);
                }
            }
        }

        /// <summary>
        /// Updates specific landblocks within a chunk by modifying buffer data
        /// </summary>
        private void UpdateLandblocksInChunk(TerrainChunk chunk, List<uint> landblockIds) {
            var chunkX = chunk.LandblockX / _chunkSizeInLandblocks;
            var chunkY = chunk.LandblockY / _chunkSizeInLandblocks;
            var chunkId = GetChunkId(chunkX, chunkY);

            if (!_chunkInfo.TryGetValue(chunkId, out var chunkInfo)) {
                Console.WriteLine($"Warning: No chunk info found for chunk {chunkId}");
                return;
            }

            var tempVertices = new VertexLandscape[VerticesPerLandblock];
            var tempIndices = new uint[IndicesPerLandblock];

            foreach (var landblockId in landblockIds) {
                var landblockX = landblockId >> 8;
                var landblockY = landblockId & 0xFF;

                // Calculate the landblock's position within the chunk
                var localLandblockX = landblockX - chunk.LandblockX;
                var localLandblockY = landblockY - chunk.LandblockY;

                // Skip if landblock is outside this chunk's actual bounds
                if (localLandblockX >= chunkInfo.ActualLandblockCountX ||
                    localLandblockY >= chunkInfo.ActualLandblockCountY) {
                    continue;
                }

                // Calculate the correct buffer offset using actual chunk dimensions
                var landblockIndex = (int)(localLandblockY * chunkInfo.ActualLandblockCountX + localLandblockX);
                var vertexOffset = landblockIndex * VerticesPerLandblock;
                var indexOffset = landblockIndex * IndicesPerLandblock;

                // Bounds checking
                if (vertexOffset + VerticesPerLandblock > chunkInfo.ActualVertexCount) {
                    Console.WriteLine($"Warning: Vertex update would exceed buffer bounds. Offset: {vertexOffset}, Buffer size: {chunkInfo.ActualVertexCount}");
                    continue;
                }

                if (indexOffset + IndicesPerLandblock > chunkInfo.ActualIndexCount) {
                    Console.WriteLine($"Warning: Index update would exceed buffer bounds. Offset: {indexOffset}, Buffer size: {chunkInfo.ActualIndexCount}");
                    continue;
                }

                // Generate new geometry for this landblock
                GenerateLandblockGeometry(landblockX, landblockY, landblockId, tempVertices, tempIndices, (uint)vertexOffset);

                // Update the vertex buffer data
                UpdateVertexBufferRange(chunk.VertexBuffer, tempVertices, vertexOffset);

                // Update the index buffer data  
                UpdateIndexBufferRange(chunk.IndexBuffer, tempIndices, indexOffset, (uint)vertexOffset);
            }
        }

        /// <summary>
        /// Generates geometry for a single landblock
        /// </summary>
        private void GenerateLandblockGeometry(uint landblockX, uint landblockY, uint landblockId,
                                             VertexLandscape[] vertices, uint[] indices, uint baseVertexIndex) {
            var landblockData = _terrain.GetLandblock((ushort)landblockId);
            if (landblockData == null) {
                // Clear the arrays for missing landblocks
                Array.Clear(vertices, 0, vertices.Length);
                Array.Clear(indices, 0, indices.Length);
                return;
            }

            uint currentVertexIndex = 0;  // Start from 0 in the temp arrays
            uint currentIndexPosition = 0;

            float baseLandblockX = landblockX * LandblockLength;
            float baseLandblockY = landblockY * LandblockLength;

            var verticesSpan = vertices.AsSpan();
            var indicesSpan = indices.AsSpan();

            // Generate all cells for this landblock
            for (uint cellY = 0; cellY < LandblockEdgeCellCount; cellY++) {
                for (uint cellX = 0; cellX < LandblockEdgeCellCount; cellX++) {
                    GenerateCell(
                        baseLandblockX, baseLandblockY, cellX, cellY,
                        landblockData, landblockId,
                        ref currentVertexIndex, ref currentIndexPosition,
                        verticesSpan, indicesSpan
                    );
                }
            }

            for (int i = 0; i < currentIndexPosition; i++) {
                indices[i] += baseVertexIndex;
            }
        }

        /// <summary>
        /// Updates a range of vertices in the vertex buffer
        /// </summary>
        private void UpdateVertexBufferRange(IVertexBuffer buffer, VertexLandscape[] newVertices, int offset) {
            if (buffer == null || newVertices == null || newVertices.Length == 0) return;

            int byteOffset = offset * VertexLandscape.Size;
            buffer.SetSubData(newVertices, byteOffset);
        }

        /// <summary>
        /// Updates a range of indices in the index buffer
        /// </summary>
        private void UpdateIndexBufferRange(IIndexBuffer buffer, uint[] newIndices, int offset, uint baseVertexIndex) {
            if (buffer == null || newIndices == null || newIndices.Length == 0) return;

            int byteOffset = offset * sizeof(uint);
            buffer.SetSubData(newIndices, byteOffset);
        }

        /// <summary>
        /// Updates a specific landblock more efficiently
        /// </summary>
        /// <param name="landblockX">Landblock X coordinate</param>
        /// <param name="landblockY">Landblock Y coordinate</param>
        public void UpdateLandblock(uint landblockX, uint landblockY) {
            var landblockId = (landblockX << 8) | landblockY;
            var modifiedLandblocks = new Dictionary<uint, Vector2> {
                { landblockId, new Vector2(landblockX, landblockY) }
            };

            UpdateModifiedLandblocks(modifiedLandblocks);
        }

        /// <summary>
        /// Updates multiple landblocks efficiently
        /// </summary>
        /// <param name="landblockCoordinates">List of landblock coordinates to update</param>
        public void UpdateLandblocks(List<Vector2> landblockCoordinates) {
            var modifiedLandblocks = new Dictionary<uint, Vector2>();

            foreach (var coord in landblockCoordinates) {
                var landblockId = ((uint)coord.X << 8) | (uint)coord.Y;
                modifiedLandblocks[landblockId] = coord;
            }

            UpdateModifiedLandblocks(modifiedLandblocks);
        }

        public static void GetCellRotation(LandSurfaceManager landSurf, uint landblockID, TerrainEntry[] terrain, uint x, uint y, ref uint surfNum, ref TextureMergeInfo.Rotation rotation) {
            var globalCellX = (int)((landblockID >> 8) + x);
            var globalCellY = (int)((landblockID & 0xFF) + y);

            // Indices for SW/SE/NE/NW
            var i = (int)(9 * x + y);
            var t1 = terrain[i].Type;
            var r1 = terrain[i].Road;

            var j = (int)(9 * (x + 1) + y);
            var t2 = terrain[j].Type;
            var r2 = terrain[j].Road;

            var t3 = terrain[j + 1].Type;
            var r3 = terrain[j + 1].Road;

            var t4 = terrain[i + 1].Type;
            var r4 = terrain[i + 1].Road;

            var palCodes = new List<uint> { GetPalCode(r1, r2, r3, r4, t1, t2, t3, t4) };

            landSurf.SelectTerrain(globalCellX, globalCellY, out surfNum, out rotation, palCodes);
        }

        public static uint GetPalCode(int r1, int r2, int r3, int r4, int t1, int t2, int t3, int t4) {
            var terrainBits = t1 << 15 | t2 << 10 | t3 << 5 | t4;
            var roadBits = r1 << 26 | r2 << 24 | r3 << 22 | r4 << 20;
            var sizeBits = 1 << 28;
            return (uint)(sizeBits | roadBits | terrainBits);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CalculateVertexNormals(ref VertexLandscape v0, ref VertexLandscape v1,
                                            ref VertexLandscape v2, ref VertexLandscape v3) {
            // Approximate normal using three points (SW, SE, NW)
            var edge1 = v1.Position - v0.Position;
            var edge2 = v3.Position - v0.Position;
            var normal = Vector3.Normalize(Vector3.Cross(edge1, edge2));

            v0.Normal = normal;
            v1.Normal = normal;
            v2.Normal = normal;
            v3.Normal = normal;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TerrainEntry GetTerrainEntryForCell(TerrainEntry[] landblockData, uint cellX, uint cellY) {
            var heightIndex = (int)(cellX * 9 + cellY);
            return landblockData != null && heightIndex < landblockData.Length
                ? landblockData[heightIndex]
                : new TerrainEntry(0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CalculateSplitDirection(uint landblockX, uint cellX, uint landblockY, uint cellY) {
            uint seedA = (landblockX * 8 + cellX) * 214614067u;
            uint seedB = (landblockY * 8 + cellY) * 1109124029u;
            uint magicA = seedA + 1813693831u;
            uint magicB = seedB;
            float splitDir = (float)(magicA - magicB - 1369149221u);

            return (splitDir * 2.3283064e-10f) >= 0.5f;
        }

        /// <summary>
        /// Calculates the height (Z value) at a given world position using bilinear interpolation
        /// </summary>
        /// <param name="worldX">World X coordinate</param>
        /// <param name="worldY">World Y coordinate</param>
        /// <returns>The interpolated height at the given position, or 0 if the position is outside valid terrain</returns>
        public float GetHeightAtPosition(float worldX, float worldY) {
            // Convert world coordinates to landblock coordinates
            uint landblockX = (uint)Math.Floor(worldX / LandblockLength);
            uint landblockY = (uint)Math.Floor(worldY / LandblockLength);

            // Check if the landblock is within map bounds
            if (landblockX >= MapSize || landblockY >= MapSize) {
                return 0f; // Outside map bounds
            }

            // Get the landblock data
            var landblockID = (uint)((landblockX << 8) | landblockY);
            var landblockData = _terrain.GetLandblock((ushort)landblockID);

            if (landblockData == null) {
                return 0f; // No terrain data available
            }

            // Calculate position within the landblock (0-192 range)
            float localX = worldX - (landblockX * LandblockLength);
            float localY = worldY - (landblockY * LandblockLength);

            // Convert to cell coordinates (0-8 range, where 8 cells per landblock edge)
            float cellX = localX / CellSize;
            float cellY = localY / CellSize;

            // Get the cell indices (0-7 range for actual cells)
            uint cellIndexX = (uint)Math.Floor(cellX);
            uint cellIndexY = (uint)Math.Floor(cellY);

            // Clamp to valid cell range
            cellIndexX = Math.Min(cellIndexX, LandblockEdgeCellCount - 1);
            cellIndexY = Math.Min(cellIndexY, LandblockEdgeCellCount - 1);

            // Calculate interpolation factors within the cell (0-1 range)
            float fracX = cellX - cellIndexX;
            float fracY = cellY - cellIndexY;

            // Get heights for the four corners of the cell
            var heightSW = GetHeightFromTerrainData(landblockData, cellIndexX, cellIndexY);
            var heightSE = GetHeightFromTerrainData(landblockData, cellIndexX + 1, cellIndexY);
            var heightNW = GetHeightFromTerrainData(landblockData, cellIndexX, cellIndexY + 1);
            var heightNE = GetHeightFromTerrainData(landblockData, cellIndexX + 1, cellIndexY + 1);

            // Perform bilinear interpolation
            // First interpolate along X axis
            float heightS = heightSW + (heightSE - heightSW) * fracX; // South edge
            float heightN = heightNW + (heightNE - heightNW) * fracX; // North edge

            // Then interpolate along Y axis
            float finalHeight = heightS + (heightN - heightS) * fracY;

            return finalHeight;
        }

        /// <summary>
        /// Gets the height value from terrain data for the specified vertex position
        /// </summary>
        /// <param name="landblockData">The terrain data for the landblock</param>
        /// <param name="vertexX">Vertex X coordinate (0-8)</param>
        /// <param name="vertexY">Vertex Y coordinate (0-8)</param>
        /// <returns>The height at the specified vertex position</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetHeightFromTerrainData(TerrainEntry[] landblockData, uint vertexX, uint vertexY) {
            // Clamp coordinates to valid range (landblocks have 9x9 height points, 0-8)
            vertexX = Math.Min(vertexX, 8);
            vertexY = Math.Min(vertexY, 8);

            // Calculate index into the height array (9x9 grid)
            var heightIndex = (int)(vertexX * 9 + vertexY);

            if (heightIndex >= 0 && heightIndex < landblockData.Length) {
                var terrainEntry = landblockData[heightIndex];
                return _region.LandDefs.LandHeightTable[terrainEntry.Height];
            }

            return 0f; // Default height if index is out of bounds
        }

        public void Dispose() {
            foreach (var chunk in _chunks.Values) {
                chunk.Dispose();
            }
            _chunks.Clear();
            _chunkInfo.Clear();
        }
    }
}