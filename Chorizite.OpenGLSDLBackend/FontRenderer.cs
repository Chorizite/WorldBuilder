using Chorizite.Core.Render;
using Chorizite.Core.Render.Vertex;
using FontStashSharp.Interfaces;
using Silk.NET.Core.Native;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Rectangle = System.Drawing.Rectangle;
using VertexPositionColorTexture = FontStashSharp.Interfaces.VertexPositionColorTexture;

namespace Chorizite.OpenGLSDLBackend {
    public class Texture2DManager : ITexture2DManager {
        private readonly OpenGLRenderer _renderer;

        public Texture2DManager(OpenGLRenderer renderer) {
            _renderer = renderer;
        }

        public object CreateTexture(int width, int height) {
            var texture = _renderer.GraphicsDevice.CreateTexture(Core.Render.Enums.TextureFormat.RGBA8, width, height, new byte[width * height * 4]);

            return texture;
        }

        public Point GetTextureSize(object texture) {
            var t = (Core.Render.ITexture)texture;
            return new Point(t.Width, t.Height);
        }

        public void SetTextureData(object texture, Rectangle bounds, byte[] data) {
            var t = (Core.Render.ITexture)texture;

            t.SetData(new Core.Render.Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height), data);
        }
    }

    internal class FontRenderer : IFontRenderer, IDisposable {
        private const int MAX_SPRITES = 10048;
        private const int MAX_VERTICES = MAX_SPRITES * 4;
        private const int MAX_INDICES = MAX_SPRITES * 6;

        private readonly IVertexBuffer _vertexBuffer;
        private readonly IIndexBuffer _indexBuffer;
        private readonly IVertexArray _vao;
        private readonly VertexPositionColorTexture[] _vertexData = new VertexPositionColorTexture[MAX_VERTICES];
        private object? _lastTexture;
        private int _vertexIndex = 0;
        private readonly OpenGLRenderer _renderer;
        private readonly Texture2DManager _textureManager;

        public ITexture2DManager TextureManager => _textureManager;

        private static readonly uint[] indexData = GenerateIndexArray();

        public unsafe FontRenderer(OpenGLRenderer renderer) {
            _renderer = renderer;
            _textureManager = new Texture2DManager(renderer);

            _vertexBuffer = _renderer.GraphicsDevice.CreateVertexBuffer(MAX_VERTICES * sizeof(VertexPositionColorTexture), Core.Render.Enums.BufferUsage.Dynamic);
            _indexBuffer = _renderer.GraphicsDevice.CreateIndexBuffer(MAX_INDICES * sizeof(uint), Core.Render.Enums.BufferUsage.Dynamic);

            _indexBuffer.SetData(indexData);

            _vao = _renderer.GraphicsDevice.CreateArrayBuffer(_vertexBuffer, Chorizite.Core.Render.Vertex.VertexPositionColorTexture.Format);
        }

        public void DrawQuad(object texture, ref VertexPositionColorTexture topLeft, ref VertexPositionColorTexture topRight, ref VertexPositionColorTexture bottomLeft, ref VertexPositionColorTexture bottomRight) {
            if (_lastTexture != texture) {
                Flush();
            }

            _vertexData[_vertexIndex++] = topLeft;
            _vertexData[_vertexIndex++] = topRight;
            _vertexData[_vertexIndex++] = bottomLeft;
            _vertexData[_vertexIndex++] = bottomRight;

            _lastTexture = texture;
        }

        public unsafe void Flush() {
            if (_vertexIndex == 0 || _lastTexture == null) {
                return;
            }

            _vertexBuffer.SetData(_vertexData.Select(v => {
                return new Chorizite.Core.Render.Vertex.VertexPositionColorTexture(v.Position, new ColorVec(v.Color.R / 255f, v.Color.G / 255f, v.Color.B / 255f, v.Color.A / 255f), v.TextureCoordinate);
            }).ToArray());

            var texture = (Core.Render.ITexture)_lastTexture;
            texture.Bind();

            _renderer.GraphicsDevice.DrawElements(Core.Render.Enums.PrimitiveType.TriangleList, (_vertexIndex * 6 / 4), 0);
            _vertexIndex = 0;
            texture.Unbind();
        }

        private static uint[] GenerateIndexArray() {
            var result = new uint[MAX_INDICES];
            for (uint i = 0, j = 0; i < MAX_INDICES; i += 6, j += 4) {
                result[i] = j;
                result[i + 1] = j + 1;
                result[i + 2] = j + 2;
                result[i + 3] = j + 3;
                result[i + 4] = j + 2;
                result[i + 5] = j + 1;
            }
            return result;
        }

        public void Dispose() => Dispose(true);

        protected virtual void Dispose(bool disposing) {
            if (!disposing) {
                return;
            }

            _vao.Dispose();
            _vertexBuffer.Dispose();
            _indexBuffer.Dispose();
        }
    }
}