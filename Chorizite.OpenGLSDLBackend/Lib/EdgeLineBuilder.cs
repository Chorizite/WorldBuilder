using System.Numerics;
using DatReaderWriter.Types;

namespace Chorizite.OpenGLSDLBackend.Lib {
    public static class EdgeLineBuilder {
        public static List<Vector3> BuildEdgeLines(CellStruct cellStruct) {
            var edgeMap = new Dictionary<EdgeKey, List<Edge>>();
            
            foreach (var kvp in cellStruct.Polygons) {
                var polyIdx = kvp.Key;
                var vertexIds = kvp.Value.VertexIds;

                var v0 = cellStruct.VertexArray.Vertices[(ushort)vertexIds[0]].Origin;
                
                // AC polys can either be triangles or triangle fans
                for (var i = 1; i < vertexIds.Count - 1; i++) {
                    var v1 = cellStruct.VertexArray.Vertices[(ushort)vertexIds[i]].Origin;
                    var v2 = cellStruct.VertexArray.Vertices[(ushort)vertexIds[i + 1]].Origin;

                    AddEdge(edgeMap, polyIdx, v0, v1);
                    AddEdge(edgeMap, polyIdx, v1, v2);
                    AddEdge(edgeMap, polyIdx, v2, v0);
                }
            }

            var output = new List<Vector3>();
            var processedEdges = new HashSet<EdgeKey>();

            foreach (var kvp in edgeMap) {
                var edgeKey = kvp.Key;
                var edgeList = kvp.Value;

                if (processedEdges.Contains(edgeKey)) continue;

                processedEdges.Add(edgeKey);

                if (edgeList.Count == 2) {
                    var poly1 = cellStruct.Polygons[edgeList[0].PolyIdx];
                    var poly2 = cellStruct.Polygons[edgeList[1].PolyIdx];

                    if (HaveSameTexture(poly1, poly2) && IsCoplanar(poly1, poly2, cellStruct))
                        continue;
                }

                output.Add(edgeList[0].P0);
                output.Add(edgeList[0].P1);
            }
            
            return output;
        }

        private static void AddEdge(Dictionary<EdgeKey, List<Edge>> edgeMap, ushort polyIdx, Vector3 p0, Vector3 p1) {
            var key = new EdgeKey(p0, p1);
            var edge = new Edge(polyIdx, p0, p1);

            if (!edgeMap.ContainsKey(key))
                edgeMap[key] = new List<Edge>();

            edgeMap[key].Add(edge);
        }

        private static bool HaveSameTexture(Polygon a, Polygon b) {
            return a.PosSurface == b.PosSurface;
        }

        private static Vector3 CalculateNormal(Polygon poly, CellStruct cellStruct) {
            var vertexIds = poly.VertexIds;
            var verts = cellStruct.VertexArray.Vertices;

            var v0 = verts[(ushort)vertexIds[0]];
            var v1 = verts[(ushort)vertexIds[1]];
            var v2 = verts[(ushort)vertexIds[2]];
            
            var edge1 = v1.Origin - v0.Origin;
            var edge2 = v2.Origin - v0.Origin;
            return Vector3.Normalize(Vector3.Cross(edge1, edge2));
        }

        private static bool IsCoplanar(Polygon a, Polygon b, CellStruct cellStruct) {
            var normA = CalculateNormal(a, cellStruct);
            var normB = CalculateNormal(b, cellStruct);

            var dp = Vector3.Dot(normA, normB);

            // If dot product is 1 or -1, normals are parallel (coplanar)
            // Allow for both same and opposite facing normals
            const float tolerance = 0.01f;
            return Math.Abs(Math.Abs(dp) - 1) < tolerance;
        }

        private class Edge {
            public ushort PolyIdx { get; }
            public Vector3 P0 { get; }
            public Vector3 P1 { get; }

            public Edge(ushort polyIdx, Vector3 p0, Vector3 p1) {
                PolyIdx = polyIdx;
                P0 = p0;
                P1 = p1;
            }
        }

        private class EdgeKey : IEquatable<EdgeKey> {
            private readonly Vector3 _p0;
            private readonly Vector3 _p1;

            public EdgeKey(Vector3 p0, Vector3 p1) {
                if (CompareVector3(p0, p1) > 0) {
                    _p0 = p1;
                    _p1 = p0;
                }
                else {
                    _p0 = p0;
                    _p1 = p1;
                }
            }

            public bool Equals(EdgeKey? e) {
                if (e == null) return false;
                return _p0 == e._p0 && _p1 == e._p1;
            }

            public override int GetHashCode() {
                return HashCode.Combine(_p0, _p1);
            }

            private static int CompareVector3(Vector3 a, Vector3 b) {
                if (a.X != b.X) return a.X.CompareTo(b.X);
                if (a.Y != b.Y) return a.Y.CompareTo(b.Y);
                return a.Z.CompareTo(b.Z);
            }
        }
    }
}
