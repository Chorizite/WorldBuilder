using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Lib;
using Chorizite.Core.Render;
using Chorizite.Core.Lib;
using DatReaderWriter.Types;

namespace Chorizite.OpenGLSDLBackend.Lib {
    public unsafe class DebugRenderer : IDebugRenderer, IDisposable {
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

            _vao = graphicsDevice.SharedDebugVAO;
            _quadVbo = graphicsDevice.SharedQuadVBO;
            _instanceVbo = graphicsDevice.SharedDebugInstanceVBO;
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

        public void DrawBox(WorldBuilder.Shared.Lib.BoundingBox box, Vector4 color) {
            DrawBox(box, Matrix4x4.Identity, color);
        }

        public void DrawBox(WorldBuilder.Shared.Lib.BoundingBox box, Matrix4x4 transform, Vector4 color) {
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

        /// <summary>
        /// Draws a circle of line segments around an axis at the given center.
        /// </summary>
        public void DrawCircle(Vector3 center, float radius, Vector3 axis, Vector4 color, int segments = 32, float thickness = 2.0f) {
            // Build a coordinate system on the plane perpendicular to the axis
            axis = Vector3.Normalize(axis);
            var tangent1 = Vector3.Cross(axis, MathF.Abs(Vector3.Dot(axis, Vector3.UnitX)) < 0.9f ? Vector3.UnitX : Vector3.UnitY);
            tangent1 = Vector3.Normalize(tangent1);
            var tangent2 = Vector3.Cross(axis, tangent1);

            for (int i = 0; i < segments; i++) {
                float angle1 = (float)i / segments * MathF.PI * 2;
                float angle2 = (float)(i + 1) / segments * MathF.PI * 2;

                var p1 = center + (tangent1 * MathF.Cos(angle1) + tangent2 * MathF.Sin(angle1)) * radius;
                var p2 = center + (tangent1 * MathF.Cos(angle2) + tangent2 * MathF.Sin(angle2)) * radius;

                DrawLine(p1, p2, color, thickness);
            }
        }

        /// <summary>
        /// Draws a line with an arrowhead at the end.
        /// </summary>
        public void DrawArrow(Vector3 start, Vector3 end, Vector4 color, float headLength = 0.3f, float thickness = 2.0f) {
            DrawLine(start, end, color, thickness);

            var dir = Vector3.Normalize(end - start);

            // Build perpendicular vectors for the arrowhead
            var perp1 = Vector3.Cross(dir, MathF.Abs(Vector3.Dot(dir, Vector3.UnitX)) < 0.9f ? Vector3.UnitX : Vector3.UnitY);
            perp1 = Vector3.Normalize(perp1);
            var perp2 = Vector3.Cross(dir, perp1);

            float headWidth = headLength * 0.4f;
            var headBase = end - dir * headLength;

            DrawLine(end, headBase + perp1 * headWidth, color, thickness);
            DrawLine(end, headBase - perp1 * headWidth, color, thickness);
            DrawLine(end, headBase + perp2 * headWidth, color, thickness);
            DrawLine(end, headBase - perp2 * headWidth, color, thickness);
        }

        public void DrawCylinder(Vector3 start, Vector3 end, float radius, Vector4 color) {
            var dir = end - start;
            if (dir.LengthSquared() < 0.0001f) return;
            var axis = Vector3.Normalize(dir);
            DrawCircle(start, radius, axis, color);
            DrawCircle(end, radius, axis, color);
            DrawLine(start, end, color);
        }

        public void DrawCone(Vector3 origin, Vector3 direction, float length, float radius, Vector4 color) {
            DrawCircle(origin, radius, direction, color);
            DrawLine(origin, origin + direction * length, color);
        }

        public void DrawTorus(Vector3 center, Vector3 axis, float radius, float tubeRadius, Vector4 color) {
            DrawCircle(center, radius, axis, color);
        }

        public void DrawPlane(Vector3 origin, Vector3 axis1, Vector3 axis2, float size, Vector4 color) {
            DrawLine(origin, origin + axis1 * size, color);
            DrawLine(origin, origin + axis2 * size, color);
            DrawLine(origin + axis1 * size, origin + axis1 * size + axis2 * size, color);
            DrawLine(origin + axis2 * size, origin + axis1 * size + axis2 * size, color);
        }

        public void DrawCenterBox(Vector3 center, float size, Vector4 color) {
            DrawBox(new WorldBuilder.Shared.Lib.BoundingBox(center - new Vector3(size / 2), center + new Vector3(size / 2)), color);
        }

        public void DrawPie(Vector3 center, float radius, Vector3 axis, Vector3 startAxis, float angle, Vector4 color) {
            DrawCircle(center, radius, axis, color);
        }

        public void Render(Matrix4x4 view, Matrix4x4 projection, bool depthTest = true) {
            if (_lineInstances.Count == 0 || _shader == null) return;

            _shader.Bind();
            _shader.SetUniform("uViewportSize", new Vector2(_graphicsDevice.Viewport.Width, _graphicsDevice.Viewport.Height));

            _gl.Disable(EnableCap.CullFace);
            if (depthTest) {
                _gl.Enable(EnableCap.DepthTest);
                _gl.DepthFunc(GLEnum.Lequal);
                _gl.DepthMask(true);
            } else {
                _gl.Disable(EnableCap.DepthTest);
                _gl.DepthMask(false);
            }
            _gl.ColorMask(true, true, true, false);
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            _gl.BindVertexArray(_vao);

            _gl.BindBuffer(GLEnum.ArrayBuffer, _instanceVbo);
            var instanceSpan = CollectionsMarshal.AsSpan(_lineInstances);
            var dataSize = (nuint)(_lineInstances.Count * Marshal.SizeOf<LineInstance>());
            
            _gl.BufferData(GLEnum.ArrayBuffer, dataSize, null, GLEnum.StreamDraw);
            fixed (void* ptr = instanceSpan) {
                _gl.BufferSubData(GLEnum.ArrayBuffer, 0, dataSize, ptr);
            }

            _gl.DrawArraysInstanced(GLEnum.Triangles, 0, 6, (uint)_lineInstances.Count);

            _lineInstances.Clear();
            _gl.BindVertexArray(0);
            _gl.DepthFunc(GLEnum.Less);
        }

        public void Dispose() {
            _lineInstances.Clear();
        }
    }
}
