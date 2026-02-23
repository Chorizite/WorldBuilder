using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using WorldBuilder.Shared.Models;
using Chorizite.Core.Render;
using Chorizite.Core.Lib;
using DatReaderWriter.Types;

namespace Chorizite.OpenGLSDLBackend.Lib {
    public unsafe class DebugRenderer : IDisposable {
        private readonly GL _gl;
        private readonly OpenGLGraphicsDevice _graphicsDevice;
        private uint _vbo;
        private uint _vao;
        private IShader? _shader;

        [StructLayout(LayoutKind.Sequential)]
        private struct DebugVertex {
            public Vector3 Position;
            public Vector4 Color;
        }

        private readonly List<DebugVertex> _vertices = new();

        public DebugRenderer(GL gl, OpenGLGraphicsDevice graphicsDevice) {
            _gl = gl;
            _graphicsDevice = graphicsDevice;

            _gl.GenVertexArrays(1, out _vao);
            _gl.GenBuffers(1, out _vbo);

            _gl.BindVertexArray(_vao);
            _gl.BindBuffer(GLEnum.ArrayBuffer, _vbo);

            // Position
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 3, GLEnum.Float, false, (uint)Marshal.SizeOf<DebugVertex>(), (void*)0);
            // Color
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 4, GLEnum.Float, false, (uint)Marshal.SizeOf<DebugVertex>(), (void*)Marshal.OffsetOf<DebugVertex>("Color"));

            _gl.BindVertexArray(0);
        }

        public void SetShader(IShader shader) {
            _shader = shader;
        }

        public void DrawLine(Vector3 start, Vector3 end, Vector4 color) {
            _vertices.Add(new DebugVertex { Position = start, Color = color });
            _vertices.Add(new DebugVertex { Position = end, Color = color });
        }

        public void DrawBox(BoundingBox box, Vector4 color) {
            var min = box.Min;
            var max = box.Max;

            // Bottom
            DrawLine(new Vector3(min.X, min.Y, min.Z), new Vector3(max.X, min.Y, min.Z), color);
            DrawLine(new Vector3(max.X, min.Y, min.Z), new Vector3(max.X, max.Y, min.Z), color);
            DrawLine(new Vector3(max.X, max.Y, min.Z), new Vector3(min.X, max.Y, min.Z), color);
            DrawLine(new Vector3(min.X, max.Y, min.Z), new Vector3(min.X, min.Y, min.Z), color);

            // Top
            DrawLine(new Vector3(min.X, min.Y, max.Z), new Vector3(max.X, min.Y, max.Z), color);
            DrawLine(new Vector3(max.X, min.Y, max.Z), new Vector3(max.X, max.Y, max.Z), color);
            DrawLine(new Vector3(max.X, max.Y, max.Z), new Vector3(min.X, max.Y, max.Z), color);
            DrawLine(new Vector3(min.X, max.Y, max.Z), new Vector3(min.X, min.Y, max.Z), color);

            // Verticals
            DrawLine(new Vector3(min.X, min.Y, min.Z), new Vector3(min.X, min.Y, max.Z), color);
            DrawLine(new Vector3(max.X, min.Y, min.Z), new Vector3(max.X, min.Y, max.Z), color);
            DrawLine(new Vector3(max.X, max.Y, min.Z), new Vector3(max.X, max.Y, max.Z), color);
            DrawLine(new Vector3(min.X, max.Y, min.Z), new Vector3(min.X, max.Y, max.Z), color);
        }

        public void DrawSphere(Vector3 center, float radius, Vector4 color, int segments = 16) {
            for (int i = 0; i < segments; i++) {
                float angle1 = (float)i / segments * MathF.PI * 2;
                float angle2 = (float)(i + 1) / segments * MathF.PI * 2;

                // XY Circle
                DrawLine(
                    center + new Vector3(MathF.Cos(angle1) * radius, MathF.Sin(angle1) * radius, 0),
                    center + new Vector3(MathF.Cos(angle2) * radius, MathF.Sin(angle2) * radius, 0),
                    color);

                // XZ Circle
                DrawLine(
                    center + new Vector3(MathF.Cos(angle1) * radius, 0, MathF.Sin(angle1) * radius),
                    center + new Vector3(MathF.Cos(angle2) * radius, 0, MathF.Sin(angle2) * radius),
                    color);

                // YZ Circle
                DrawLine(
                    center + new Vector3(0, MathF.Cos(angle1) * radius, MathF.Sin(angle1) * radius),
                    center + new Vector3(0, MathF.Cos(angle2) * radius, MathF.Sin(angle2) * radius),
                    color);
            }
        }

        public void Render(Matrix4x4 view, Matrix4x4 projection) {
            if (_vertices.Count == 0 || _shader == null) return;

            _shader.Bind();
            _shader.SetUniform("uView", view);
            _shader.SetUniform("uProjection", projection);
            _shader.SetUniform("uModel", Matrix4x4.Identity);

            _gl.LineWidth(2.0f);
            _gl.BindVertexArray(_vao);
            _gl.BindBuffer(GLEnum.ArrayBuffer, _vbo);
            
            var vertexSpan = CollectionsMarshal.AsSpan(_vertices);
            _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(_vertices.Count * Marshal.SizeOf<DebugVertex>()), vertexSpan, GLEnum.StreamDraw);

            _gl.DrawArrays(GLEnum.Lines, 0, (uint)_vertices.Count);

            _gl.LineWidth(1.0f);
            _vertices.Clear();
            _gl.BindVertexArray(0);
        }

        public void Dispose() {
            if (_vbo != 0) _gl.DeleteBuffer(_vbo);
            if (_vao != 0) _gl.DeleteVertexArray(_vao);
        }
    }
}
