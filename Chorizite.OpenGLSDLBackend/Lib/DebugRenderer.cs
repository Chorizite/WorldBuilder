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
        private uint _quadVbo;
        private uint _instanceVbo;
        private uint _vao;
        private IShader? _shader;

        [StructLayout(LayoutKind.Sequential)]
        private struct LineInstance {
            public Vector3 Start;
            public Vector3 End;
            public Vector4 Color;
            public float Thickness;
        }

        private readonly List<LineInstance> _lineInstances = new();

        public DebugRenderer(GL gl, OpenGLGraphicsDevice graphicsDevice) {
            _gl = gl;
            _graphicsDevice = graphicsDevice;

            _gl.GenVertexArrays(1, out _vao);
            _gl.GenBuffers(1, out _quadVbo);
            _gl.GenBuffers(1, out _instanceVbo);

            _gl.BindVertexArray(_vao);

            // Unit quad vertices for two triangles (0 to 1 for length, -0.5 to 0.5 for thickness)
            float[] quadVertices = {
                0.0f, -0.5f,
                1.0f, -0.5f,
                1.0f,  0.5f,
                0.0f, -0.5f,
                1.0f,  0.5f,
                0.0f,  0.5f
            };

            _gl.BindBuffer(GLEnum.ArrayBuffer, _quadVbo);
            fixed (float* pQuad = quadVertices) {
                _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(quadVertices.Length * sizeof(float)), pQuad, GLEnum.StaticDraw);
            }

            // Quad Pos attribute (location 0)
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 2, GLEnum.Float, false, 2 * sizeof(float), (void*)0);

            // Instance attributes
            _gl.BindBuffer(GLEnum.ArrayBuffer, _instanceVbo);
            
            // aStart (location 1)
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 3, GLEnum.Float, false, (uint)Marshal.SizeOf<LineInstance>(), (void*)0);
            _gl.VertexAttribDivisor(1, 1);

            // aEnd (location 2)
            _gl.EnableVertexAttribArray(2);
            _gl.VertexAttribPointer(2, 3, GLEnum.Float, false, (uint)Marshal.SizeOf<LineInstance>(), (void*)Marshal.OffsetOf<LineInstance>("End"));
            _gl.VertexAttribDivisor(2, 1);

            // aColor (location 3)
            _gl.EnableVertexAttribArray(3);
            _gl.VertexAttribPointer(3, 4, GLEnum.Float, false, (uint)Marshal.SizeOf<LineInstance>(), (void*)Marshal.OffsetOf<LineInstance>("Color"));
            _gl.VertexAttribDivisor(3, 1);

            // aThickness (location 4)
            _gl.EnableVertexAttribArray(4);
            _gl.VertexAttribPointer(4, 1, GLEnum.Float, false, (uint)Marshal.SizeOf<LineInstance>(), (void*)Marshal.OffsetOf<LineInstance>("Thickness"));
            _gl.VertexAttribDivisor(4, 1);

            _gl.BindVertexArray(0);
        }

        public void SetShader(IShader shader) {
            _shader = shader;
        }

        public void DrawLine(Vector3 start, Vector3 end, Vector4 color, float thickness = 2.0f) {
            _lineInstances.Add(new LineInstance { 
                Start = start, 
                End = end, 
                Color = color, 
                Thickness = thickness 
            });
        }

        public void DrawBox(BoundingBox box, Vector4 color) {
            DrawBox(box, Matrix4x4.Identity, color);
        }

        public void DrawBox(BoundingBox box, Matrix4x4 transform, Vector4 color) {
            var min = box.Min;
            var max = box.Max;

            var corners = new Vector3[8];
            corners[0] = new Vector3(min.X, min.Y, min.Z);
            corners[1] = new Vector3(max.X, min.Y, min.Z);
            corners[2] = new Vector3(max.X, max.Y, min.Z);
            corners[3] = new Vector3(min.X, max.Y, min.Z);
            corners[4] = new Vector3(min.X, min.Y, max.Z);
            corners[5] = new Vector3(max.X, min.Y, max.Z);
            corners[6] = new Vector3(max.X, max.Y, max.Z);
            corners[7] = new Vector3(min.X, max.Y, max.Z);

            for (int i = 0; i < 8; i++) {
                corners[i] = Vector3.Transform(corners[i], transform);
            }

            // Bottom
            DrawLine(corners[0], corners[1], color);
            DrawLine(corners[1], corners[2], color);
            DrawLine(corners[2], corners[3], color);
            DrawLine(corners[3], corners[0], color);

            // Top
            DrawLine(corners[4], corners[5], color);
            DrawLine(corners[5], corners[6], color);
            DrawLine(corners[6], corners[7], color);
            DrawLine(corners[7], corners[4], color);

            // Verticals
            DrawLine(corners[0], corners[4], color);
            DrawLine(corners[1], corners[5], color);
            DrawLine(corners[2], corners[6], color);
            DrawLine(corners[3], corners[7], color);
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
            if (_lineInstances.Count == 0 || _shader == null) return;

            _shader.Bind();
            _shader.SetUniform("uView", view);
            _shader.SetUniform("uProjection", projection);
            _shader.SetUniform("uViewportSize", new Vector2(_graphicsDevice.Viewport.Width, _graphicsDevice.Viewport.Height));

            _gl.Disable(EnableCap.CullFace);
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(GLEnum.Lequal);
            _gl.DepthMask(true);
            _gl.ColorMask(true, true, true, false);
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            _gl.BindVertexArray(_vao);
            
            _gl.BindBuffer(GLEnum.ArrayBuffer, _instanceVbo);
            var instanceSpan = CollectionsMarshal.AsSpan(_lineInstances);
            _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(_lineInstances.Count * Marshal.SizeOf<LineInstance>()), instanceSpan, GLEnum.StreamDraw);

            _gl.DrawArraysInstanced(GLEnum.Triangles, 0, 6, (uint)_lineInstances.Count);

            _lineInstances.Clear();
            _gl.BindVertexArray(0);
            _gl.DepthFunc(GLEnum.Less);
        }

        public void Dispose() {
            if (_quadVbo != 0) _gl.DeleteBuffer(_quadVbo);
            if (_instanceVbo != 0) _gl.DeleteBuffer(_instanceVbo);
            if (_vao != 0) _gl.DeleteVertexArray(_vao);
        }
    }
}
