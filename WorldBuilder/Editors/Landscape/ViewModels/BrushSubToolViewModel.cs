using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Editors.Landscape.ViewModels {
    public partial class BrushSubToolViewModel : SubToolViewModelBase {
        public override string Name => "Brush";
        public override string IconGlyph => "🖌️";

        [ObservableProperty]
        private float _brushRadius = 5f;

        [ObservableProperty]
        private TerrainTextureType _selectedTerrainType = TerrainTextureType.Volcano1;

        [ObservableProperty]
        private List<TerrainTextureType> _availableTerrainTypes;
        private bool _isPainting;
        private TerrainRaycast.TerrainRaycastHit _currentHitPosition;
        private TerrainRaycast.TerrainRaycastHit _lastHitPosition;

        public BrushSubToolViewModel(TerrainEditingContext context) : base(context) {
            _availableTerrainTypes = System.Enum.GetValues<TerrainTextureType>().ToList();
        }

        partial void OnBrushRadiusChanged(float value) {
            // Update the actual tool settings
            if (value < 0.5f) BrushRadius = 0.5f;
            if (value > 50f) BrushRadius = 50f;
        }

        public override void OnActivated() {
            Context.ActiveVertices.Clear();
            _lastHitPosition = _currentHitPosition = new TerrainRaycast.TerrainRaycastHit();
        }

        public override void OnDeactivated() {
            if (_isPainting) {
                _isPainting = false;
            }
        }

        public override void Update(double deltaTime) {
            if (Vector3.Distance(_currentHitPosition.NearestVertice,_lastHitPosition.NearestVertice) < 0.01f) return;

            Context.ActiveVertices.Clear();
            var affected = GetAffectedVertices(_currentHitPosition.NearestVertice, BrushRadius, Context);

            foreach (var (_, _, pos) in affected) {
                Context.ActiveVertices.Add(new Vector2(pos.X, pos.Y));
            }

            _lastHitPosition = _currentHitPosition;
        }

        public override bool HandleMouseUp(MouseState mouseState) {
            if (_isPainting && !mouseState.LeftPressed) {
                _isPainting = false;
                return true;
            }
            return false;
        }

        public override bool HandleMouseMove(MouseState mouseState) {
            if (!mouseState.IsOverTerrain || !mouseState.TerrainHit.HasValue) return false;

            var hitResult = mouseState.TerrainHit.Value;
            _currentHitPosition = hitResult;

            if (_isPainting) {
                PaintTextureBrush(hitResult.NearestVertice, SelectedTerrainType, BrushRadius, Context);

                return true;
            }

            return false;
        }

        public override bool HandleMouseDown(MouseState mouseState) {
            if (!mouseState.IsOverTerrain || !mouseState.TerrainHit.HasValue || !mouseState.LeftPressed) return false;

            _isPainting = true;
            var hitResult = mouseState.TerrainHit.Value;
            PaintTextureBrush(
                hitResult.NearestVertice,
                SelectedTerrainType,
                BrushRadius,
                Context);

            return true;
        }

        private void PaintTextureBrush(
            Vector3 centerPosition,
            TerrainTextureType terrainType,
            float brushRadius,
            TerrainEditingContext context) {

            var affected = GetAffectedVertices(centerPosition, brushRadius, context);
            var modifiedLandblocks = new HashSet<ushort>();
            var landblockDataCache = new Dictionary<ushort, TerrainEntry[]>();

            foreach (var (lbId, vIndex, _) in affected) {
                if (!landblockDataCache.TryGetValue(lbId, out var data)) {
                    data = context.TerrainDocument.GetLandblock(lbId);
                    if (data == null) continue;
                    landblockDataCache[lbId] = data;
                }

                data[vIndex] = data[vIndex] with { Type = (byte)terrainType };
                modifiedLandblocks.Add(lbId);
            }

            var allModifiedLandblocks = new HashSet<ushort>();
            foreach (var lbId in modifiedLandblocks) {
                var data = landblockDataCache[lbId];
                context.TerrainDocument.UpdateLandblock(lbId, data, out var modified);
                foreach (var mod in modified) {
                    allModifiedLandblocks.Add(mod);
                }
            }

            foreach (var lbId in allModifiedLandblocks) {
                var data = context.TerrainDocument.GetLandblock(lbId);
                context.TerrainDocument.SynchronizeEdgeVerticesFor(lbId, data, new HashSet<ushort>());
            }

            foreach (var lbId in allModifiedLandblocks) {
                context.MarkLandblockModified(lbId);
            }
        }

        private List<(ushort LandblockId, int VertexIndex, Vector3 Position)> GetAffectedVertices(
            Vector3 position,
            float radius,
            TerrainEditingContext context) {

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
    }
}