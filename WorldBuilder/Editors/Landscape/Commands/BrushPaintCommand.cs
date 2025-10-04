using DatReaderWriter.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Editors.Landscape.Commands {
    public class BrushPaintCommand : ICommand {
        private readonly TerrainEditingContext _context;
        private readonly TerrainTextureType _terrainType;
        private readonly Dictionary<ushort, (int VertexIndex, byte OriginalType, byte NewType)[]> _changes;

        public string Description => $"Paint {Enum.GetName(typeof(TerrainTextureType), _terrainType)}";

        public bool CanExecute => true;
        public bool CanUndo => true;

        // Constructor for single point (used in original code, kept for compatibility)
        public BrushPaintCommand(TerrainEditingContext context, Vector3 centerPosition, TerrainTextureType terrainType, float brushRadius) {
            _context = context;
            _terrainType = terrainType;
            _changes = new Dictionary<ushort, (int, byte, byte)[]>();
            var affected = GetAffectedVertices(centerPosition, brushRadius, context);
            var landblockDataCache = new Dictionary<ushort, TerrainEntry[]>();

            foreach (var (lbId, vIndex, _) in affected) {
                if (!landblockDataCache.TryGetValue(lbId, out var data)) {
                    data = _context.TerrainDocument.GetLandblock(lbId);
                    if (data == null) continue;
                    landblockDataCache[lbId] = data;
                }

                if (!_changes.ContainsKey(lbId)) {
                    _changes[lbId] = new List<(int, byte, byte)>().ToArray();
                }

                var currentChanges = _changes[lbId].ToList();
                currentChanges.Add((vIndex, data[vIndex].Type, (byte)_terrainType));
                _changes[lbId] = currentChanges.ToArray();
            }
        }

        // New constructor for accumulated changes
        public BrushPaintCommand(TerrainEditingContext context, TerrainTextureType terrainType,
            Dictionary<ushort, List<(int VertexIndex, byte OriginalType, byte NewType)>> changes) {
            _context = context;
            _terrainType = terrainType;
            _changes = changes.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToArray()
            );
        }

        public bool Execute() {
            var modifiedLandblocks = new HashSet<ushort>();
            var landblockDataCache = new Dictionary<ushort, TerrainEntry[]>();

            foreach (var (lbId, changes) in _changes) {
                if (!landblockDataCache.TryGetValue(lbId, out var data)) {
                    data = _context.TerrainDocument.GetLandblock(lbId);
                    if (data == null) continue;
                    landblockDataCache[lbId] = data;
                }

                foreach (var (vIndex, _, newType) in changes) {
                    data[vIndex] = data[vIndex] with { Type = newType };
                }

                _context.TerrainDocument.UpdateLandblock(lbId, data, out var modified);
                modifiedLandblocks.UnionWith(modified);
            }

            foreach (var lbId in modifiedLandblocks) {
                var data = _context.TerrainDocument.GetLandblock(lbId);
                _context.TerrainDocument.SynchronizeEdgeVerticesFor(lbId, data, new HashSet<ushort>());
                _context.MarkLandblockModified(lbId);
            }

            return true;
        }

        public bool Undo() {
            var modifiedLandblocks = new HashSet<ushort>();
            foreach (var (lbId, changes) in _changes) {
                var data = _context.TerrainDocument.GetLandblock(lbId);
                if (data == null) continue;

                foreach (var (vIndex, originalType, _) in changes) {
                    data[vIndex] = data[vIndex] with { Type = originalType };
                }

                _context.TerrainDocument.UpdateLandblock(lbId, data, out var modified);
                modifiedLandblocks.UnionWith(modified);
            }

            foreach (var lbId in modifiedLandblocks) {
                var data = _context.TerrainDocument.GetLandblock(lbId);
                _context.TerrainDocument.SynchronizeEdgeVerticesFor(lbId, data, new HashSet<ushort>());
                _context.MarkLandblockModified(lbId);
            }

            return true;
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
            int mapSize = (int)255;

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
                    float z = context.TerrainSystem.DataManager.GetHeightAtPosition(vert2D.X, vert2D.Y);
                    Vector3 vertPos = new Vector3(vert2D.X, vert2D.Y, z);
                    affected.Add((lbId, vertexIndex, vertPos));

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
    }
}