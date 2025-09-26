using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using Chorizite.OpenGLSDLBackend.Extensions;
using Silk.NET.OpenGL;
using System.Runtime.InteropServices;

namespace Chorizite.OpenGLSDLBackend {
    internal class ManagedGLTextureArray : ITextureArray {
        private readonly bool[] _usedLayers;
        private readonly GL GL;
        private static int _nextId = 0;
        public int Slot { get; } = _nextId++;

        /// <inheritdoc />
        public int Width { get; private set; }

        /// <inheritdoc />
        public int Height { get; private set; }

        /// <inheritdoc />
        public int Size { get; private set; }

        /// <inheritdoc />
        public TextureFormat Format { get; private set; }

        /// <inheritdoc />
        public nint NativePtr { get; private set; }

        public ManagedGLTextureArray(OpenGLGraphicsDevice graphicsDevice, TextureFormat format, int width, int height, int size) {
            Format = format;
            Width = width;
            Height = height;
            Size = size;

            _usedLayers = new bool[size];

            GL = graphicsDevice.GL;

            NativePtr = (nint)GL.GenTexture();
            GLHelpers.CheckErrors();

            GL.BindTexture(GLEnum.Texture2DArray, (uint)NativePtr);
            GLHelpers.CheckErrors();

            GL.TexStorage3D(GLEnum.Texture2DArray, 8u, format.ToGL(), (uint)width, (uint)height, (uint)size);
            GLHelpers.CheckErrors();

            GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapNearest);
            GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            GLHelpers.CheckErrors();
        }

        /// <inheritdoc />
        public void Bind(int slot = 0) {
            GL.ActiveTexture(GLEnum.Texture0 + slot);
            GLHelpers.CheckErrors();
            GL.BindTexture(GLEnum.Texture2DArray, (uint)NativePtr);
            GLHelpers.CheckErrors();
        }

        /// <inheritdoc />
        public unsafe int AddLayer(byte[] data) {
            Bind();

            for (int i = 0; i < _usedLayers.Length; i++) {
                if (!_usedLayers[i]) {
                    UpdateLayer(i, data);
                    _usedLayers[i] = true;
                    return i;
                }
            }

            throw new InvalidOperationException("No free layers available in texture array.");
        }

        /// <inheritdoc />
        public void UpdateLayer(int layer, byte[] data) {
            Bind();
            unsafe {
                GCHandle pinnedArray = GCHandle.Alloc(data, GCHandleType.Pinned);
                var pixelsPtr = (void*)pinnedArray.AddrOfPinnedObject();
                GL.TexSubImage3D(GLEnum.Texture2DArray, 0, 0, 0, layer, (uint)Width, (uint)Height, 1, Format.ToPixelFormat(), PixelType.UnsignedByte, pixelsPtr);
                pinnedArray.Free();
                GLHelpers.CheckErrors();

                GL.GenerateMipmap(GLEnum.Texture2DArray);
                GLHelpers.CheckErrors();
            }
        }

        /// <inheritdoc />
        public void RemoveLayer(int layer) {
            if (layer < 0 || layer >= Size) {
                throw new ArgumentOutOfRangeException(nameof(layer), "Layer index is out of range.");
            }
            if (!_usedLayers[layer]) {
                throw new InvalidOperationException("Layer is already free.");
            }
            _usedLayers[layer] = false;
        }

        /// <inheritdoc />
        public void Unbind() {

        }

        /// <inheritdoc />
        public void Dispose() {
            GL.DeleteTexture((uint)NativePtr);
        }
    }
}