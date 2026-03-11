using Chorizite.Core.Render;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using WorldBuilder.Shared.Lib;

namespace Chorizite.OpenGLSDLBackend.Lib {
    public unsafe class BackendGizmoDrawer : IDebugRenderer, IDisposable {
        private readonly GL _gl;
        private readonly OpenGLGraphicsDevice _graphicsDevice;

        private uint _vao;
        private uint _vbo;
        private uint _ebo;
        private IShader? _shader;

        // Draw calls
        private abstract class GizmoDrawCmd { }
        private class DrawCylinderCmd : GizmoDrawCmd {
            public Vector3 Start, End; public float Radius; public Vector4 Color;
        }
        private class DrawConeCmd : GizmoDrawCmd {
            public Vector3 Origin, Direction; public float Length, Radius; public Vector4 Color;
        }
        private class DrawTorusCmd : GizmoDrawCmd {
            public Vector3 Center, Axis; public float Radius, TubeRadius; public Vector4 Color;
        }
        private class DrawBoxCmd : GizmoDrawCmd {
            public Vector3 Center; public float Size; public Vector4 Color;
        }
        private class DrawPieCmd : GizmoDrawCmd {
            public Vector3 Center, Axis, StartAxis; public float Radius, Angle; public Vector4 Color;
        }
        private class DrawQuadCmd : GizmoDrawCmd {
            public Vector3 Origin, Axis1, Axis2; public float Size; public Vector4 Color;
        }
        private class DrawLineCmd : GizmoDrawCmd {
            public Vector3 Start, End; public Vector4 Color; public float Thickness;
        }

        private readonly List<GizmoDrawCmd> _commands = new();
        private readonly DebugRenderer _debugRendererFallback;

        // Mesh offsets
        private int _cylIndexOffset, _cylIndexCount;
        private int _coneIndexOffset, _coneIndexCount;
        private int _torusIndexOffset, _torusIndexCount;
        private int _boxIndexOffset, _boxIndexCount;
        private int _discIndexOffset, _discIndexCount;
        private int _quadIndexOffset, _quadIndexCount;

        [StructLayout(LayoutKind.Sequential)]
        private struct Vertex {
            public Vector3 Position;
            public Vector3 Normal;
        }

        public BackendGizmoDrawer(GL gl, OpenGLGraphicsDevice graphicsDevice, DebugRenderer debugFallback) {
            _gl = gl;
            _graphicsDevice = graphicsDevice;
            _debugRendererFallback = debugFallback;

            BuildGeometry();
        }

        public void SetShader(IShader shader) {
            _shader = shader;
        }

        private void BuildGeometry() {
            var vertices = new List<Vertex>();
            var indices = new List<uint>();

            void AddMesh(List<Vertex> v, List<uint> i, out int idxOffset, out int idxCount) {
                uint baseVertex = (uint)vertices.Count;
                idxOffset = indices.Count * sizeof(uint);
                idxCount = i.Count;

                vertices.AddRange(v);
                foreach (var idx in i) {
                    indices.Add(baseVertex + idx);
                }
            }

            // 1) Cylinder (radius=1, length=1 along +Z, start at Z=0, end at Z=1)
            BuildCylinder(16, out var cylV, out var cylI);
            AddMesh(cylV, cylI, out _cylIndexOffset, out _cylIndexCount);

            // 2) Cone (radius=1 at Z=0, tip at Z=1)
            BuildCone(16, out var coneV, out var coneI);
            AddMesh(coneV, coneI, out _coneIndexOffset, out _coneIndexCount);

            // 3) Torus (majorR=1, minorR=0.03, lay on XY plane)
            BuildTorus(32, 12, 1.0f, 0.03f, out var torusV, out var torusI);
            AddMesh(torusV, torusI, out _torusIndexOffset, out _torusIndexCount);

            // 4) Box (size=1)
            BuildBox(out var boxV, out var boxI);
            AddMesh(boxV, boxI, out _boxIndexOffset, out _boxIndexCount);

            // 5) Disc (radius=1, normalZ)
            BuildDisc(64, out var discV, out var discI);
            AddMesh(discV, discI, out _discIndexOffset, out _discIndexCount);

            // 6) Quad
            BuildQuad(out var quadV, out var quadI);
            AddMesh(quadV, quadI, out _quadIndexOffset, out _quadIndexCount);

            // Create OpenGL buffers
            _gl.GenVertexArrays(1, out _vao);
            _gl.GenBuffers(1, out _vbo);
            _gl.GenBuffers(1, out _ebo);

            _gl.BindVertexArray(_vao);

            _gl.BindBuffer(GLEnum.ArrayBuffer, _vbo);
            var vSpan = CollectionsMarshal.AsSpan(vertices);
            _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(vertices.Count * sizeof(Vertex)), vSpan, GLEnum.StaticDraw);

            _gl.BindBuffer(GLEnum.ElementArrayBuffer, _ebo);
            var iSpan = CollectionsMarshal.AsSpan(indices);
            _gl.BufferData(GLEnum.ElementArrayBuffer, (nuint)(indices.Count * sizeof(uint)), iSpan, GLEnum.StaticDraw);

            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 3, GLEnum.Float, false, (uint)sizeof(Vertex), (void*)0);

            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 3, GLEnum.Float, false, (uint)sizeof(Vertex), (void*)12);

            _gl.BindVertexArray(0);
        }

        private void BuildCylinder(int segments, out List<Vertex> vertices, out List<uint> indices) {
            vertices = new List<Vertex>();
            indices = new List<uint>();

            // Center of bottom/top
            int bottomCenterIdx = 0;
            int topCenterIdx = 1;

            vertices.Add(new Vertex { Position = new Vector3(0, 0, 0), Normal = new Vector3(0, 0, -1) });
            vertices.Add(new Vertex { Position = new Vector3(0, 0, 1), Normal = new Vector3(0, 0, 1) });

            int baseIdx = 2;
            for (int i = 0; i < segments; i++) {
                float angle = (i / (float)segments) * MathF.PI * 2f;
                float nx = MathF.Cos(angle);
                float ny = MathF.Sin(angle);

                // Bottom and top cap vertices
                int bCap = baseIdx + i * 4;
                int tCap = baseIdx + i * 4 + 1;
                // Side vertices
                int bSide = baseIdx + i * 4 + 2;
                int tSide = baseIdx + i * 4 + 3;

                vertices.Add(new Vertex { Position = new Vector3(nx, ny, 0), Normal = new Vector3(0, 0, -1) });
                vertices.Add(new Vertex { Position = new Vector3(nx, ny, 1), Normal = new Vector3(0, 0, 1) });
                vertices.Add(new Vertex { Position = new Vector3(nx, ny, 0), Normal = new Vector3(nx, ny, 0) });
                vertices.Add(new Vertex { Position = new Vector3(nx, ny, 1), Normal = new Vector3(nx, ny, 0) });

                int nextI = (i + 1) % segments;
                int nextBCap = baseIdx + nextI * 4;
                int nextTCap = baseIdx + nextI * 4 + 1;
                int nextBSide = baseIdx + nextI * 4 + 2;
                int nextTSide = baseIdx + nextI * 4 + 3;

                // Bottom cap
                indices.Add((uint)bottomCenterIdx); indices.Add((uint)nextBCap); indices.Add((uint)bCap);
                // Top cap
                indices.Add((uint)topCenterIdx); indices.Add((uint)tCap); indices.Add((uint)nextTCap);
                // Side
                indices.Add((uint)bSide); indices.Add((uint)nextBSide); indices.Add((uint)nextTSide);
                indices.Add((uint)bSide); indices.Add((uint)nextTSide); indices.Add((uint)tSide);
            }
        }

        private void BuildCone(int segments, out List<Vertex> vertices, out List<uint> indices) {
            vertices = new List<Vertex>();
            indices = new List<uint>();

            int bottomCenterIdx = 0;
            int tipIdx = 1;
            vertices.Add(new Vertex { Position = new Vector3(0, 0, 0), Normal = new Vector3(0, 0, -1) });
            vertices.Add(new Vertex { Position = new Vector3(0, 0, 1), Normal = new Vector3(0, 0, 1) }); // approximate normal at tip

            int baseIdx = 2;
            for (int i = 0; i < segments; i++) {
                float angle = (i / (float)segments) * MathF.PI * 2f;
                float nx = MathF.Cos(angle);
                float ny = MathF.Sin(angle);
                float nz = 1.0f; // Simplified normal, not perfect but okay for gizmo

                int bCap = baseIdx + i * 2;
                int bSide = baseIdx + i * 2 + 1;

                vertices.Add(new Vertex { Position = new Vector3(nx, ny, 0), Normal = new Vector3(0, 0, -1) });
                vertices.Add(new Vertex { Position = new Vector3(nx, ny, 0), Normal = Vector3.Normalize(new Vector3(nx, ny, nz)) });

                int nextI = (i + 1) % segments;
                int nextBCap = baseIdx + nextI * 2;
                int nextBSide = baseIdx + nextI * 2 + 1;

                indices.Add((uint)bottomCenterIdx); indices.Add((uint)nextBCap); indices.Add((uint)bCap);

                indices.Add((uint)bSide); indices.Add((uint)nextBSide); indices.Add((uint)tipIdx);
            }
        }

        private void BuildTorus(int mainSegments, int tubeSegments, float mainRadius, float tubeRadius, out List<Vertex> vertices, out List<uint> indices) {
            vertices = new List<Vertex>();
            indices = new List<uint>();

            for (int i = 0; i <= mainSegments; i++) {
                float u = i / (float)mainSegments * MathF.PI * 2f;
                float cosU = MathF.Cos(u);
                float sinU = MathF.Sin(u);

                for (int j = 0; j <= tubeSegments; j++) {
                    float v = j / (float)tubeSegments * MathF.PI * 2f;
                    float cosV = MathF.Cos(v);
                    float sinV = MathF.Sin(v);

                    float x = (mainRadius + tubeRadius * cosV) * cosU;
                    float y = (mainRadius + tubeRadius * cosV) * sinU;
                    float z = tubeRadius * sinV;

                    float nx = cosV * cosU;
                    float ny = cosV * sinU;
                    float nz = sinV;

                    vertices.Add(new Vertex { Position = new Vector3(x, y, z), Normal = new Vector3(nx, ny, nz) });
                }
            }

            for (int i = 0; i < mainSegments; i++) {
                for (int j = 0; j < tubeSegments; j++) {
                    int first = (i * (tubeSegments + 1)) + j;
                    int second = first + tubeSegments + 1;

                    indices.Add((uint)first);
                    indices.Add((uint)second);
                    indices.Add((uint)(first + 1));

                    indices.Add((uint)second);
                    indices.Add((uint)(second + 1));
                    indices.Add((uint)(first + 1));
                }
            }
        }

        private void BuildDisc(int segments, out List<Vertex> vertices, out List<uint> indices) {
            vertices = new List<Vertex>();
            indices = new List<uint>();

            int centerIdx = 0;
            vertices.Add(new Vertex { Position = new Vector3(0, 0, 0), Normal = new Vector3(0, 0, 1) });

            for (int i = 0; i < segments; i++) {
                float a = (i / (float)segments) * MathF.PI * 2f;
                vertices.Add(new Vertex { Position = new Vector3(MathF.Cos(a), MathF.Sin(a), 0), Normal = new Vector3(0, 0, 1) });
            }

            for (int i = 0; i < segments; i++) {
                indices.Add((uint)centerIdx);
                indices.Add((uint)(1 + i));
                indices.Add((uint)(1 + (i + 1) % segments));
            }
        }

        private void BuildBox(out List<Vertex> vertices, out List<uint> indices) {
            vertices = new List<Vertex>();
            indices = new List<uint>();

            var positions = new Vector3[] {
                new(-0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, -0.5f), new(0.5f, 0.5f, -0.5f), new(-0.5f, 0.5f, -0.5f), // bottom
                new(-0.5f, -0.5f,  0.5f), new(0.5f, -0.5f,  0.5f), new(0.5f, 0.5f,  0.5f), new(-0.5f, 0.5f,  0.5f)  // top
            };

            var faces = new int[][] {
                new[] { 0, 1, 2, 3 }, // Front
                new[] { 4, 7, 6, 5 }, // Back
                new[] { 0, 4, 5, 1 }, // Bottom
                new[] { 3, 2, 6, 7 }, // Top
                new[] { 0, 3, 7, 4 }, // Left
                new[] { 1, 5, 6, 2 }  // Right
            };

            var normals = new Vector3[] {
                new(0, 0, -1), new(0, 0, 1), new(0, -1, 0), new(0, 1, 0), new(-1, 0, 0), new(1, 0, 0)
            };

            for (int f = 0; f < 6; f++) {
                int baseVert = vertices.Count;
                var n = normals[f];
                for (int i = 0; i < 4; i++) {
                    vertices.Add(new Vertex { Position = positions[faces[f][i]], Normal = n });
                }
                indices.Add((uint)baseVert); indices.Add((uint)(baseVert + 1)); indices.Add((uint)(baseVert + 2));
                indices.Add((uint)baseVert); indices.Add((uint)(baseVert + 2)); indices.Add((uint)(baseVert + 3));
            }
        }

        private void BuildQuad(out List<Vertex> vertices, out List<uint> indices) {
            vertices = new List<Vertex>();
            indices = new List<uint>();
            vertices.Add(new Vertex { Position = new Vector3(0, 0, 0), Normal = new Vector3(0, 0, 1) });
            vertices.Add(new Vertex { Position = new Vector3(1, 0, 0), Normal = new Vector3(0, 0, 1) });
            vertices.Add(new Vertex { Position = new Vector3(1, 1, 0), Normal = new Vector3(0, 0, 1) });
            vertices.Add(new Vertex { Position = new Vector3(0, 1, 0), Normal = new Vector3(0, 0, 1) });
            indices.Add(0); indices.Add(1); indices.Add(2);
            indices.Add(0); indices.Add(2); indices.Add(3);
        }

        public void DrawLine(Vector3 start, Vector3 end, Vector4 color, float thickness = 2f) {
            _commands.Add(new DrawLineCmd { Start = start, End = end, Color = color, Thickness = thickness });
        }

        public void DrawCircle(Vector3 center, float radius, Vector3 axis, Vector4 color, int segments = 32, float thickness = 2f) {
            // fallback
            _debugRendererFallback.DrawCircle(center, radius, axis, color, segments, thickness);
        }

        public void DrawArrow(Vector3 start, Vector3 end, Vector4 color, float headLength = 0.3f, float thickness = 2f) {
            _debugRendererFallback.DrawArrow(start, end, color, headLength, thickness);
        }

        public void DrawBox(WorldBuilder.Shared.Lib.BoundingBox box, Vector4 color) {
            _debugRendererFallback.DrawBox(box, color);
        }

        public void DrawBox(WorldBuilder.Shared.Lib.BoundingBox box, Matrix4x4 transform, Vector4 color) {
            _debugRendererFallback.DrawBox(box, transform, color);
        }

        public void DrawSphere(Vector3 center, float radius, Vector4 color, int segments = 16) {
            _debugRendererFallback.DrawSphere(center, radius, color, segments);
        }

        public void DrawCylinder(Vector3 start, Vector3 end, float radius, Vector4 color) {
            _commands.Add(new DrawCylinderCmd { Start = start, End = end, Radius = radius, Color = color });
        }

        public void DrawCone(Vector3 origin, Vector3 direction, float length, float radius, Vector4 color) {
            _commands.Add(new DrawConeCmd { Origin = origin, Direction = direction, Length = length, Radius = radius, Color = color });
        }

        public void DrawTorus(Vector3 center, Vector3 axis, float radius, float tubeRadius, Vector4 color) {
            _commands.Add(new DrawTorusCmd { Center = center, Axis = axis, Radius = radius, TubeRadius = tubeRadius, Color = color });
        }

        public void DrawPlane(Vector3 origin, Vector3 axis1, Vector3 axis2, float size, Vector4 color) {
            _commands.Add(new DrawQuadCmd { Origin = origin, Axis1 = axis1, Axis2 = axis2, Size = size, Color = color });
        }

        public void DrawCenterBox(Vector3 center, float size, Vector4 color) {
            _commands.Add(new DrawBoxCmd { Center = center, Size = size, Color = color });
        }

        public void DrawPie(Vector3 center, float radius, Vector3 axis, Vector3 startAxis, float angle, Vector4 color) {
            _commands.Add(new DrawPieCmd { Center = center, Radius = radius, Axis = axis, StartAxis = startAxis, Angle = angle, Color = color });
        }

        public void Render(Matrix4x4 view, Matrix4x4 projection) {
            if (_commands.Count == 0 || _shader == null) return;

            _shader.Bind();

            // Setup state
            _gl.Disable(EnableCap.DepthTest);
            _gl.DepthMask(false);
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            _gl.BindVertexArray(_vao);

            foreach (var cmd in _commands) {
                if (cmd is DrawLineCmd lineCmd) {
                    _debugRendererFallback.DrawLine(lineCmd.Start, lineCmd.End, lineCmd.Color, lineCmd.Thickness);
                }
                else if (cmd is DrawCylinderCmd cylCmd) {
                    var dir = cylCmd.End - cylCmd.Start;
                    float len = dir.Length();
                    if (len <= 0.0001f) continue;
                    var normDir = dir / len;

                    var model = CreateAlignZMatrix(cylCmd.Start, normDir);
                    var scale = Matrix4x4.CreateScale(cylCmd.Radius, cylCmd.Radius, len);
                    _shader.SetUniform("uModel", scale * model);
                    _shader.SetUniform("uBaseColor", cylCmd.Color);
                    _shader.SetUniform("uIsPie", 0);

                    _gl.DrawElements(GLEnum.Triangles, (uint)_cylIndexCount, GLEnum.UnsignedInt, (void*)_cylIndexOffset);
                }
                else if (cmd is DrawConeCmd coneCmd) {
                    var model = CreateAlignZMatrix(coneCmd.Origin, Vector3.Normalize(coneCmd.Direction));
                    var scale = Matrix4x4.CreateScale(coneCmd.Radius, coneCmd.Radius, coneCmd.Length);
                    _shader.SetUniform("uModel", scale * model);
                    _shader.SetUniform("uBaseColor", coneCmd.Color);
                    _shader.SetUniform("uIsPie", 0);

                    _gl.DrawElements(GLEnum.Triangles, (uint)_coneIndexCount, GLEnum.UnsignedInt, (void*)_coneIndexOffset);
                }
                else if (cmd is DrawTorusCmd torusCmd) {
                    var model = CreateAlignZMatrix(torusCmd.Center, Vector3.Normalize(torusCmd.Axis));
                    var scale = Matrix4x4.CreateScale(torusCmd.Radius, torusCmd.Radius, torusCmd.Radius);
                    _shader.SetUniform("uModel", scale * model);
                    _shader.SetUniform("uBaseColor", torusCmd.Color);
                    _shader.SetUniform("uIsPie", 0);

                    _gl.DrawElements(GLEnum.Triangles, (uint)_torusIndexCount, GLEnum.UnsignedInt, (void*)_torusIndexOffset);
                }
                else if (cmd is DrawBoxCmd boxCmd) {
                    var model = Matrix4x4.CreateTranslation(boxCmd.Center);
                    var scale = Matrix4x4.CreateScale(boxCmd.Size);
                    _shader.SetUniform("uModel", scale * model);
                    _shader.SetUniform("uBaseColor", boxCmd.Color);
                    _shader.SetUniform("uIsPie", 0);

                    _gl.DrawElements(GLEnum.Triangles, (uint)_boxIndexCount, GLEnum.UnsignedInt, (void*)_boxIndexOffset);
                }
                else if (cmd is DrawQuadCmd quadCmd) {
                    var normal = Vector3.Normalize(Vector3.Cross(quadCmd.Axis1, quadCmd.Axis2));
                    var model = new Matrix4x4(
                        quadCmd.Axis1.X * quadCmd.Size, quadCmd.Axis1.Y * quadCmd.Size, quadCmd.Axis1.Z * quadCmd.Size, 0,
                        quadCmd.Axis2.X * quadCmd.Size, quadCmd.Axis2.Y * quadCmd.Size, quadCmd.Axis2.Z * quadCmd.Size, 0,
                        normal.X, normal.Y, normal.Z, 0,
                        quadCmd.Origin.X, quadCmd.Origin.Y, quadCmd.Origin.Z, 1
                    );
                    _shader.SetUniform("uModel", model);
                    _shader.SetUniform("uBaseColor", quadCmd.Color);
                    _shader.SetUniform("uIsPie", 0);

                    // Draw two-sided
                    _gl.Disable(EnableCap.CullFace);
                    _gl.DrawElements(GLEnum.Triangles, (uint)_quadIndexCount, GLEnum.UnsignedInt, (void*)_quadIndexOffset);
                    _gl.Enable(EnableCap.CullFace);
                }
                else if (cmd is DrawPieCmd pieCmd) {
                    // For the pie, we do a neat shader trick. We build a full unit disc.
                    var model = CreateAlignZMatrix(pieCmd.Center, Vector3.Normalize(pieCmd.Axis));
                    var scale = Matrix4x4.CreateScale(pieCmd.Radius, pieCmd.Radius, 1.0f);
                    _shader.SetUniform("uModel", scale * model);
                    _shader.SetUniform("uBaseColor", pieCmd.Color);

                    _shader.SetUniform("uIsPie", 1);
                    _shader.SetUniform("uPieCenter", pieCmd.Center);
                    _shader.SetUniform("uPieStartDir", pieCmd.StartAxis);
                    _shader.SetUniform("uPieAngle", pieCmd.Angle);
                    _shader.SetUniform("uPieAxis", pieCmd.Axis);

                    _gl.Disable(EnableCap.CullFace);
                    _gl.DrawElements(GLEnum.Triangles, (uint)_discIndexCount, GLEnum.UnsignedInt, (void*)_discIndexOffset);
                    _gl.Enable(EnableCap.CullFace);
                }
            }

            _shader.SetUniform("uIsPie", 0);
            _commands.Clear();
            _gl.BindVertexArray(0);
        }

        private Matrix4x4 CreateAlignZMatrix(Vector3 position, Vector3 direction) {
            var up = MathF.Abs(direction.Y) > 0.99f ? Vector3.UnitX : Vector3.UnitY;
            var right = Vector3.Normalize(Vector3.Cross(up, direction));
            var newUp = Vector3.Cross(direction, right);

            return new Matrix4x4(
                right.X, right.Y, right.Z, 0,
                newUp.X, newUp.Y, newUp.Z, 0,
                direction.X, direction.Y, direction.Z, 0,
                position.X, position.Y, position.Z, 1
            );
        }

        public void Dispose() {
            var vbo = _vbo;
            var ebo = _ebo;
            var vao = _vao;
            _graphicsDevice.QueueGLAction(gl => {
                if (vbo != 0) gl.DeleteBuffer(vbo);
                if (ebo != 0) gl.DeleteBuffer(ebo);
                if (vao != 0) gl.DeleteVertexArray(vao);
            });
        }
    }
}
