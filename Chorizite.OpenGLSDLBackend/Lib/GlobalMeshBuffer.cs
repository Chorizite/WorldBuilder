using Chorizite.Core.Render.Enums;
using Silk.NET.OpenGL;
using System;

namespace Chorizite.OpenGLSDLBackend.Lib {
    public class GlobalMeshBuffer : IDisposable {
        private readonly GL _gl;
        public uint VAO { get; private set; }
        public uint VBO { get; private set; }
        public uint IBO { get; private set; }

        private int _vboCapacity = 1024 * 1024; // 1M vertices (~32MB)
        private int _iboCapacity = 3 * 1024 * 1024; // 3M indices (~6MB)
        private int _vboOffset = 0;
        private int _iboOffset = 0;

        public GlobalMeshBuffer(GL gl) {
            _gl = gl;
            InitBuffers();
        }

        private unsafe void InitBuffers() {
            _gl.GenVertexArrays(1, out uint vao);
            VAO = vao;
            _gl.BindVertexArray(VAO);

            _gl.GenBuffers(1, out uint vbo);
            VBO = vbo;
            _gl.BindBuffer(GLEnum.ArrayBuffer, VBO);
            _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(_vboCapacity * VertexPositionNormalTexture.Size), null, GLEnum.StaticDraw);

            int stride = VertexPositionNormalTexture.Size;
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 3, GLEnum.Float, false, (uint)stride, (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 3, GLEnum.Float, false, (uint)stride, (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(2);
            _gl.VertexAttribPointer(2, 2, GLEnum.Float, false, (uint)stride, (void*)(6 * sizeof(float)));

            _gl.GenBuffers(1, out uint ibo);
            IBO = ibo;
            _gl.BindBuffer(GLEnum.ElementArrayBuffer, IBO);
            _gl.BufferData(GLEnum.ElementArrayBuffer, (nuint)(_iboCapacity * sizeof(ushort)), null, GLEnum.StaticDraw);

            _gl.BindVertexArray(0);
        }

        public unsafe (int baseVertex, int firstIndex) Append(VertexPositionNormalTexture[] vertices, ushort[] indices) {
            if (vertices.Length == 0 || indices.Length == 0) return (0, 0);

            // Check capacity
            if (_vboOffset + vertices.Length > _vboCapacity) {
                ResizeVBO(Math.Max(_vboCapacity * 2, _vboCapacity + vertices.Length));
            }
            if (_iboOffset + indices.Length > _iboCapacity) {
                ResizeIBO(Math.Max(_iboCapacity * 2, _iboCapacity + indices.Length));
            }

            int baseVertex = _vboOffset;
            int firstIndex = _iboOffset;

            _gl.BindBuffer(GLEnum.ArrayBuffer, VBO);
            fixed (VertexPositionNormalTexture* ptr = vertices) {
                _gl.BufferSubData(GLEnum.ArrayBuffer, (nint)(baseVertex * VertexPositionNormalTexture.Size), (nuint)(vertices.Length * VertexPositionNormalTexture.Size), ptr);
            }

            _gl.BindBuffer(GLEnum.ElementArrayBuffer, IBO);
            fixed (ushort* ptr = indices) {
                _gl.BufferSubData(GLEnum.ElementArrayBuffer, (nint)(firstIndex * sizeof(ushort)), (nuint)(indices.Length * sizeof(ushort)), ptr);
            }

            _vboOffset += vertices.Length;
            _iboOffset += indices.Length;

            return (baseVertex, firstIndex);
        }

        private unsafe void ResizeVBO(int newCapacity) {
            _gl.GenBuffers(1, out uint newVbo);
            _gl.BindBuffer(GLEnum.ArrayBuffer, newVbo);
            _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(newCapacity * VertexPositionNormalTexture.Size), null, GLEnum.StaticDraw);

            _gl.BindBuffer(GLEnum.CopyReadBuffer, VBO);
            _gl.BindBuffer(GLEnum.CopyWriteBuffer, newVbo);
            _gl.CopyBufferSubData(GLEnum.CopyReadBuffer, GLEnum.CopyWriteBuffer, 0, 0, (nuint)(_vboOffset * VertexPositionNormalTexture.Size));

            _gl.DeleteBuffer(VBO);
            VBO = newVbo;
            _vboCapacity = newCapacity;

            // Re-bind to VAO
            _gl.BindVertexArray(VAO);
            _gl.BindBuffer(GLEnum.ArrayBuffer, VBO);
            int stride = VertexPositionNormalTexture.Size;
            _gl.VertexAttribPointer(0, 3, GLEnum.Float, false, (uint)stride, (void*)0);
            _gl.VertexAttribPointer(1, 3, GLEnum.Float, false, (uint)stride, (void*)(3 * sizeof(float)));
            _gl.VertexAttribPointer(2, 2, GLEnum.Float, false, (uint)stride, (void*)(6 * sizeof(float)));
            _gl.BindVertexArray(0);
        }

        private unsafe void ResizeIBO(int newCapacity) {
            _gl.GenBuffers(1, out uint newIbo);
            _gl.BindBuffer(GLEnum.ElementArrayBuffer, newIbo);
            _gl.BufferData(GLEnum.ElementArrayBuffer, (nuint)(newCapacity * sizeof(ushort)), null, GLEnum.StaticDraw);

            _gl.BindBuffer(GLEnum.CopyReadBuffer, IBO);
            _gl.BindBuffer(GLEnum.CopyWriteBuffer, newIbo);
            _gl.CopyBufferSubData(GLEnum.CopyReadBuffer, GLEnum.CopyWriteBuffer, 0, 0, (nuint)(_iboOffset * sizeof(ushort)));

            _gl.DeleteBuffer(IBO);
            IBO = newIbo;
            _iboCapacity = newCapacity;

            // Re-bind to VAO
            _gl.BindVertexArray(VAO);
            _gl.BindBuffer(GLEnum.ElementArrayBuffer, IBO);
            _gl.BindVertexArray(0);
        }

        public void Dispose() {
            if (VAO != 0) _gl.DeleteVertexArray(VAO);
            if (VBO != 0) _gl.DeleteBuffer(VBO);
            if (IBO != 0) _gl.DeleteBuffer(IBO);
            VAO = VBO = IBO = 0;
        }
    }
}
