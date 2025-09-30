using Chorizite.Core.Render;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Tools.Landscape {
    public class TexturePaintingTool : ITerrainTool {
        public TerrainTextureType SelectedTerrainType { get; set; } = TerrainTextureType.Volcano1;
        public PaintSubMode PaintMode { get; private set; } = PaintSubMode.Brush;
        public float BrushRadius { get; set; } = 5f;

        private bool _isPainting = false;

        public string Name => "Texture Painting";

        public enum PaintSubMode {
            Brush,
            Bucket
        }

        public void SetPaintMode(PaintSubMode mode) {
            PaintMode = mode;
        }

        public void OnActivated(TerrainEditingContext context) {
            _isPainting = false;
        }

        public void OnDeactivated(TerrainEditingContext context) {
            // End any ongoing paint operation
            if (_isPainting) {
                context.EndOperation();
                _isPainting = false;
            }
        }

        public bool HandleMouseDown(MouseState mouseState, TerrainEditingContext context) {
            if (!mouseState.IsOverTerrain || !mouseState.TerrainHit.HasValue || !mouseState.LeftPressed) return false;

            var hitResult = mouseState.TerrainHit.Value;

            if (PaintMode == PaintSubMode.Brush) {
                _isPainting = true;

                // Begin paint operation
                context.BeginOperation($"Paint {SelectedTerrainType} Brush");

                // Capture affected landblocks before painting
                var affectedLandblocks = context.GetAffectedLandblocks(hitResult.NearestVertice, BrushRadius * 12f);
                context.CaptureTerrainState(affectedLandblocks);

                PaintTextureBrush(hitResult.NearestVertice, SelectedTerrainType, context);
            }
            else if (PaintMode == PaintSubMode.Bucket) {
                // Begin bucket fill operation
                context.BeginOperation($"Bucket Fill {SelectedTerrainType}");

                // Let FillTexture handle state capture
                FillTexture(hitResult, SelectedTerrainType, context);

                // End operation immediately for bucket fill
                context.EndOperation();
            }

            return true;
        }

        public bool HandleMouseUp(MouseState mouseState, TerrainEditingContext context) {
            if (_isPainting) {
                // End the brush painting operation
                context.EndOperation();
                _isPainting = false;
                return true;
            }
            return false;
        }

        public bool HandleMouseMove(MouseState mouseState, TerrainEditingContext context) {
            if (!mouseState.IsOverTerrain || !mouseState.TerrainHit.HasValue) return false;

            var hitResult = mouseState.TerrainHit.Value;
            context.ActiveVertices.Clear();

            if (PaintMode == PaintSubMode.Brush) {
                var affected = GetAffectedVertices(hitResult.NearestVertice, BrushRadius, context);
                foreach (var (_, _, pos) in affected) {
                    context.ActiveVertices.Add(pos);
                }

                if (_isPainting) {
                    // Continue capturing terrain state as we paint
                    var affectedLandblocks = context.GetAffectedLandblocks(hitResult.NearestVertice, BrushRadius * 12f);
                    context.CaptureTerrainState(affectedLandblocks);

                    PaintTextureBrush(hitResult.NearestVertice, SelectedTerrainType, context);
                }
            }
            else {
                context.ActiveVertices.Add(hitResult.NearestVertice);
            }

            return mouseState.LeftPressed;
        }

        public void RenderOverlay(TerrainEditingContext context, IRenderer renderer, ICamera camera, float aspectRatio) {
            // Existing rendering code
        }

        private List<(ushort LandblockId, int VertexIndex, Vector3 Position)> GetAffectedVertices(Vector3 position, float radius, TerrainEditingContext context) {
            radius = (radius * 12f) + 1f;
            var affected = new List<(ushort, int, Vector3)>();
            const float gridSpacing = 24f;
            Vector2 center2D = new Vector2(position.X, position.Y);
            float gridRadius = radius / gridSpacing + 0.5f;
            int centerGX = (int)Math.Round(center2D.X / gridSpacing);
            int centerGY = (int)Math.Round(center2D.Y / gridSpacing);
            int minGX = centerGX - (int)Math.Ceiling(gridRadius);
            int maxGX = centerGX + (int)Math.Ceiling(gridRadius);
            int minGY = centerGY - (int)Math.Ceiling(gridRadius);
            int maxGY = centerGY + (int)Math.Ceiling(gridRadius);
            int mapSize = (int)TerrainProvider.MapSize;

            for (int gx = minGX; gx <= maxGX; gx++) {
                for (int gy = minGY; gy <= maxGY; gy++) {
                    if (gx < 0 || gy < 0) continue;
                    Vector2 vert2D = new Vector2(gx * gridSpacing, gy * gridSpacing);
                    if ((vert2D - center2D).Length() > radius) continue;
                    int lbX = gx / 8;
                    int lbY = gy / 8;
                    if (lbX >= mapSize || lbY >= mapSize) continue;
                    int localVX = gx - lbX * 8;
                    int localVY = gy - lbY * 8;
                    if (localVX < 0 || localVX > 8 || localVY < 0 || localVY > 8) continue;
                    int vertexIndex = localVX * 9 + localVY;
                    ushort lbId = (ushort)((lbX << 8) | lbY);
                    float z = context.TerrainProvider.GetHeightAtPosition(vert2D.X, vert2D.Y);
                    Vector3 vertPos = new Vector3(vert2D.X, vert2D.Y, z);
                    affected.Add((lbId, vertexIndex, vertPos));

                    // Add duplicates for boundary vertices in adjacent landblocks
                    if (localVX == 0 && lbX > 0) {
                        ushort leftLbId = (ushort)(((lbX - 1) << 8) | lbY);
                        int leftVertexIndex = 8 * 9 + localVY;
                        affected.Add((leftLbId, leftVertexIndex, vertPos));
                    }
                    if (localVY == 0 && lbY > 0) {
                        ushort bottomLbId = (ushort)((lbX << 8) | (lbY - 1));
                        int bottomVertexIndex = localVX * 9 + 8;
                        affected.Add((bottomLbId, bottomVertexIndex, vertPos));
                    }
                    if (localVX == 0 && localVY == 0 && lbX > 0 && lbY > 0) {
                        ushort diagLbId = (ushort)(((lbX - 1) << 8) | (lbY - 1));
                        int diagVertexIndex = 8 * 9 + 8;
                        affected.Add((diagLbId, diagVertexIndex, vertPos));
                    }
                }
            }

            return affected.Distinct().ToList();
        }

        private void PaintTextureBrush(Vector3 centerPosition, TerrainTextureType terrainType, TerrainEditingContext context) {
            var affected = GetAffectedVertices(centerPosition, BrushRadius, context);
            var modifiedLandblocks = new HashSet<ushort>();
            var landblockDataCache = new Dictionary<ushort, TerrainEntry[]>();

            foreach (var (lbId, vIndex, _) in affected) {
                if (!landblockDataCache.TryGetValue(lbId, out var data)) {
                    data = context.Terrain.GetLandblock(lbId);
                    if (data == null) continue;
                    landblockDataCache[lbId] = data;
                }

                data[vIndex] = data[vIndex] with { Type = (byte)terrainType };
                modifiedLandblocks.Add(lbId);
            }

            var allModifiedLandblocks = new HashSet<ushort>();
            foreach (var lbId in modifiedLandblocks) {
                var data = landblockDataCache[lbId];
                context.Terrain.UpdateLandblock(lbId, data, out var modified);
                foreach (var mod in modified) {
                    allModifiedLandblocks.Add(mod);
                }
            }

            foreach (var lbId in allModifiedLandblocks) {
                var data = context.Terrain.GetLandblock(lbId);
                context.Terrain.SynchronizeEdgeVerticesFor(lbId, data, new List<ushort>());
            }

            foreach (var lbId in allModifiedLandblocks) {
                context.TrackModifiedLandblock(lbId);
            }
        }

        private void FillTexture(TerrainRaycast.TerrainRaycastHit hitResult, TerrainTextureType newType, TerrainEditingContext context) {
            uint startLbX = hitResult.LandblockX;
            uint startLbY = hitResult.LandblockY;
            uint startCellX = (uint)hitResult.CellX;
            uint startCellY = (uint)hitResult.CellY;

            uint startLbID = (startLbX << 8) | startLbY;
            var startData = context.Terrain.GetLandblock((ushort)startLbID);
            if (startData == null) return;

            int startIndex = (int)(startCellX * 9 + startCellY);
            if (startIndex >= startData.Length) return;

            byte oldType = startData[startIndex].Type;
            if ((TerrainTextureType)oldType == newType) return;

            var visited = new HashSet<(uint lbX, uint lbY, uint cellX, uint cellY)>();
            var queue = new Queue<(uint lbX, uint lbY, uint cellX, uint cellY)>();
            queue.Enqueue((startLbX, startLbY, startCellX, startCellY));

            var modifiedLandblocks = new HashSet<ushort>();
            var landblockDataCache = new Dictionary<ushort, TerrainEntry[]>();

            // Capture state for all potentially affected landblocks and their neighbors
            var allAffectedLandblocks = new HashSet<ushort>();
            while (queue.Count > 0) {
                var (lbX, lbY, cellX, cellY) = queue.Dequeue();
                if (visited.Contains((lbX, lbY, cellX, cellY))) continue;
                visited.Add((lbX, lbY, cellX, cellY));

                var lbID = (ushort)((lbX << 8) | lbY);
                if (!allAffectedLandblocks.Contains(lbID)) {
                    allAffectedLandblocks.UnionWith(context.GetNeighboringLandblockIds(lbID));
                }

                if (!landblockDataCache.TryGetValue(lbID, out var data)) {
                    data = context.Terrain.GetLandblock(lbID);
                    if (data == null) continue;
                    landblockDataCache[lbID] = data;
                }

                int index = (int)(cellX * 9 + cellY);
                if (index >= data.Length || data[index].Type != oldType) continue;

                data[index] = data[index] with { Type = (byte)newType };
                modifiedLandblocks.Add(lbID);

                // Add neighbors (4-way)
                if (cellX > 0) {
                    queue.Enqueue((lbX, lbY, cellX - 1, cellY));
                }
                else if (lbX > 0) {
                    queue.Enqueue((lbX - 1, lbY, 8, cellY));
                }
                if (cellX < 8) {
                    queue.Enqueue((lbX, lbY, cellX + 1, cellY));
                }
                else if (lbX < (uint)TerrainProvider.MapSize - 1) {
                    queue.Enqueue((lbX + 1, lbY, 0, cellY));
                }
                if (cellY > 0) {
                    queue.Enqueue((lbX, lbY, cellX, cellY - 1));
                }
                else if (lbY > 0) {
                    queue.Enqueue((lbX, lbY - 1, cellX, 8));
                }
                if (cellY < 8) {
                    queue.Enqueue((lbX, lbY, cellX, cellY + 1));
                }
                else if (lbY < (uint)TerrainProvider.MapSize - 1) {
                    queue.Enqueue((lbX, lbY + 1, cellX, 0));
                }
            }

            // Capture state for all affected landblocks and their neighbors
            context.CaptureTerrainState(allAffectedLandblocks);

            // Update modified landblocks
            var allModifiedLandblocks = new HashSet<ushort>();
            foreach (var lbID in modifiedLandblocks) {
                if (landblockDataCache.TryGetValue(lbID, out var data)) {
                    context.Terrain.UpdateLandblock(lbID, data, out var modified);
                    foreach (var mod in modified) {
                        allModifiedLandblocks.Add(mod);
                    }
                }
            }

            // Synchronize edge vertices for all modified landblocks
            foreach (var lbId in allModifiedLandblocks) {
                var data = context.Terrain.GetLandblock(lbId);
                if (data != null) {
                    context.Terrain.SynchronizeEdgeVerticesFor(lbId, data, new List<ushort>());
                    context.TrackModifiedLandblock(lbId);
                }
            }
        }
    }
}