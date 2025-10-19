using DatReaderWriter.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Editors.Landscape.Commands {
    public class PaintCommand : TerrainVertexChangeCommand {
        private readonly byte _terrainType;

        public PaintCommand(TerrainEditingContext context, TerrainTextureType terrainType, Vector3 centerPosition, float brushRadius) : base(context) {
            _terrainType = (byte)terrainType;
            CollectChanges(centerPosition, brushRadius);
        }

        public PaintCommand(TerrainEditingContext context, TerrainTextureType terrainType, Dictionary<ushort, List<(int VertexIndex, byte OriginalType, byte NewType)>> changes) : base(context) {
            _terrainType = (byte)terrainType;
            foreach (var kvp in changes) {
                _changes[kvp.Key] = kvp.Value;
            }
        }

        public override string Description => $"Paint {Enum.GetName(typeof(TerrainTextureType), _terrainType)}";

        protected override byte GetEntryValue(TerrainEntry entry) => entry.Type;
        protected override TerrainEntry SetEntryValue(TerrainEntry entry, byte value) => entry with { Type = value };

        private void CollectChanges(Vector3 position, float brushRadius) {
            var affected = GetAffectedVertices(position, brushRadius, _context);
            var landblockDataCache = new Dictionary<ushort, TerrainEntry[]>();

            foreach (var (lbId, vIndex, _) in affected) {
                if (!_changes.TryGetValue(lbId, out var list)) {
                    list = new List<(int, byte, byte)>();
                    _changes[lbId] = list;
                }

                if (list.Any(c => c.VertexIndex == vIndex)) continue;

                if (!landblockDataCache.TryGetValue(lbId, out var data)) {
                    data = _context.TerrainSystem.GetLandblockTerrain(lbId);
                    if (data == null) continue;
                    landblockDataCache[lbId] = data;
                }

                byte original = data[vIndex].Type;
                if (original == _terrainType) continue;
                list.Add((vIndex, original, _terrainType));
            }
        }

        public static List<(ushort LandblockId, int VertexIndex, Vector3 Position)> GetAffectedVertices(
            Vector3 position, float radius, TerrainEditingContext context) {
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
            int mapSize = 255;

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
                    float z = context.GetHeightAtPosition(vert2D.X, vert2D.Y);
                    Vector3 vertPos = new Vector3(vert2D.X, vert2D.Y, z);
                    affected.Add((lbId, vertexIndex, vertPos));

                    // Edge neighbor handling
                    if (localVX == 0 && lbX > 0) {
                        ushort leftLbId = (ushort)(((lbX - 1) << 8) | lbY);
                        int leftVertexIndex = 8 * 9 + localVY;
                        affected.Add((leftLbId, leftVertexIndex, vertPos));
                    }

                    if (localVX == 8 && lbX < mapSize - 1) {
                        ushort rightLbId = (ushort)(((lbX + 1) << 8) | lbY);
                        int rightVertexIndex = 0 * 9 + localVY;
                        affected.Add((rightLbId, rightVertexIndex, vertPos));
                    }

                    if (localVY == 0 && lbY > 0) {
                        ushort bottomLbId = (ushort)((lbX << 8) | (lbY - 1));
                        int bottomVertexIndex = localVX * 9 + 8;
                        affected.Add((bottomLbId, bottomVertexIndex, vertPos));
                    }

                    if (localVY == 8 && lbY < mapSize - 1) {
                        ushort topLbId = (ushort)((lbX << 8) | (lbY + 1));
                        int topVertexIndex = localVX * 9 + 0;
                        affected.Add((topLbId, topVertexIndex, vertPos));
                    }

                    if (localVX == 0 && localVY == 0 && lbX > 0 && lbY > 0) {
                        ushort diagLbId = (ushort)(((lbX - 1) << 8) | (lbY - 1));
                        int diagVertexIndex = 8 * 9 + 8;
                        affected.Add((diagLbId, diagVertexIndex, vertPos));
                    }

                    if (localVX == 8 && localVY == 0 && lbX < mapSize - 1 && lbY > 0) {
                        ushort diagLbId = (ushort)(((lbX + 1) << 8) | (lbY - 1));
                        int diagVertexIndex = 0 * 9 + 8;
                        affected.Add((diagLbId, diagVertexIndex, vertPos));
                    }

                    if (localVX == 0 && localVY == 8 && lbX > 0 && lbY < mapSize - 1) {
                        ushort diagLbId = (ushort)(((lbX - 1) << 8) | (lbY + 1));
                        int diagVertexIndex = 8 * 9 + 0;
                        affected.Add((diagLbId, diagVertexIndex, vertPos));
                    }

                    if (localVX == 8 && localVY == 8 && lbX < mapSize - 1 && lbY < mapSize - 1) {
                        ushort diagLbId = (ushort)(((lbX + 1) << 8) | (lbY + 1));
                        int diagVertexIndex = 0 * 9 + 0;
                        affected.Add((diagLbId, diagVertexIndex, vertPos));
                    }
                }
            }

            return affected.Distinct().ToList();
        }
    }
}
