using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chorizite.Core.Render.Enums;
using Chorizite.Core.Render.Vertex;
using Silk.NET.OpenGL;
using SixLabors.ImageSharp.Memory;
using VertexAttribType = Silk.NET.OpenGL.VertexAttribType;

namespace Chorizite.OpenGLSDLBackend {
    public unsafe class ManagedGLVertexArray : IVertexArray {
        private readonly OpenGLGraphicsDevice _device;
        private GL GL => _device.GL;
        private uint _vaoId = 0;

        public ManagedGLVertexArray(OpenGLGraphicsDevice device, IVertexBuffer buffer, VertexFormat format) {
            _device = device;

            // Generate the vertex array
            _vaoId = GL.GenVertexArray();
            GLHelpers.CheckErrors();

            if (_vaoId == 0) {
                throw new Exception("Failed to generate vertex array.");
            }

            SetVertexBuffer(buffer, format);
        }

        public void SetVertexBuffer(IVertexBuffer buffer, VertexFormat format) {
            GL.BindVertexArray(_vaoId);
            GLHelpers.CheckErrors();
            buffer.Bind();
            for (int i = 0; i < format.Attributes.Length; i++) {
                var attr = format.Attributes[i];
                GL.EnableVertexAttribArray((uint)i);
                GLHelpers.CheckErrors();
                GL.VertexAttribPointer((uint)i, attr.Size, Convert(attr.Type), attr.Normalized, (uint)format.Stride, attr.Offset);
                GLHelpers.CheckErrors();
            }
            GL.BindVertexArray(0);
            GLHelpers.CheckErrors();
        }

        private GLEnum Convert(Core.Render.Enums.VertexAttribType type) => type switch {
            Core.Render.Enums.VertexAttribType.Float => GLEnum.Float,
            Core.Render.Enums.VertexAttribType.Int => GLEnum.Int,
            _ => throw new NotSupportedException()
        };

        public void Bind() {
            GL.BindVertexArray(_vaoId);
            GLHelpers.CheckErrors();
        }

        public void Unbind() {
            GL.BindVertexArray(0);
            GLHelpers.CheckErrors();
        }

        public void Dispose() {
            GL.DeleteVertexArray(_vaoId);
            GLHelpers.CheckErrors();
        }
    }
}
