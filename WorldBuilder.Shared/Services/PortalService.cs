using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Numerics;

namespace WorldBuilder.Shared.Services {
    public class PortalService : IPortalService {
        private readonly IDatReaderWriter _dats;

        public PortalService(IDatReaderWriter dats) {
            _dats = dats;
        }

        public IEnumerable<PortalData> GetPortalsForLandblock(uint regionId, uint landblockId) {
            var lbId = (landblockId & 0xFFFF0000u) | 0xFFFE;

            if (!_dats.CellRegions.TryGetValue(regionId, out var cellDb)) yield break;
            if (!cellDb.TryGet<LandBlockInfo>(lbId, out var lbi)) yield break;

            for (uint i = 0; i < lbi.NumCells; i++) {
                var cellId = (landblockId & 0xFFFF0000u) | (0x0100 + i);
                foreach (var portal in GetPortalsForCell(cellDb, cellId)) {
                    yield return portal;
                }
            }
        }

        public PortalData? GetPortal(uint regionId, uint landblockId, uint cellId, uint portalIndex) {
            if (!_dats.CellRegions.TryGetValue(regionId, out var cellDb)) return null;
            
            var portals = GetPortalsForCell(cellDb, cellId).ToList();
            if (portalIndex < portals.Count) {
                return portals[(int)portalIndex];
            }
            return null;
        }

        private IEnumerable<PortalData> GetPortalsForCell(IDatDatabase cellDb, uint cellId) {
            if (cellDb.TryGet<EnvCell>(cellId, out var envCell)) {
                for (int portalIdx = 0; portalIdx < envCell.CellPortals.Count; portalIdx++) {
                    var portal = envCell.CellPortals[portalIdx];
                    if (portal.OtherCellId == 0xFFFF) {
                        // Portal to outside!
                        if (_dats.Portal.TryGet<DatReaderWriter.DBObjs.Environment>(0x0D000000u | envCell.EnvironmentId, out var environment)) {
                            if (environment.Cells.TryGetValue(envCell.CellStructure, out var cellStruct)) {
                                if (cellStruct.Polygons.TryGetValue(portal.PolygonId, out var polygon)) {
                                    var vertices = new List<Vector3>();

                                    foreach (var vertexId in polygon.VertexIds) {
                                        if (cellStruct.VertexArray.Vertices.TryGetValue((ushort)vertexId, out var vertex)) {
                                            vertices.Add(vertex.Origin);
                                        }
                                    }

                                    var transform = Matrix4x4.CreateFromQuaternion(envCell.Position.Orientation) *
                                                    Matrix4x4.CreateTranslation(envCell.Position.Origin);

                                    var worldVertices = vertices.Select(v => Vector3.Transform(v, transform)).ToArray();
                                    var pMin = new Vector3(float.MaxValue);
                                    var pMax = new Vector3(float.MinValue);
                                    foreach (var v in worldVertices) {
                                        pMin = Vector3.Min(pMin, v);
                                        pMax = Vector3.Max(pMax, v);
                                    }

                                    yield return new PortalData {
                                        CellId = cellId,
                                        PortalIndex = (uint)portalIdx,
                                        Vertices = worldVertices,
                                        BoundingBox = new BoundingBox(pMin, pMax)
                                    };
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
