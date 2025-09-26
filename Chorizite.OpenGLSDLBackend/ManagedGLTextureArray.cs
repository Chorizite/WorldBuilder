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
        private bool _needsMipmapRegeneration = false;

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

            // Calculate optimal mipmap levels
            int maxDimension = Math.Max(width, height);
            int mipLevels = (int)Math.Floor(Math.Log2(maxDimension)) + 1;

            GL.TexStorage3D(GLEnum.Texture2DArray, (uint)mipLevels, format.ToGL(), (uint)width, (uint)height, (uint)size);
            GLHelpers.CheckErrors();

            // Improved filtering parameters
            GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            // Try to enable anisotropic filtering if available
            TrySetAnisotropicFiltering();

            GLHelpers.CheckErrors();
        }

        private void TrySetAnisotropicFiltering() {
            /*
            try {
                // Check if anisotropic filtering extension is available
                string extensions = GL.GetStringS(GLEnum.Extensions);
                if (extensions.Contains("GL_EXT_texture_filter_anisotropic")) {
                    // Get max anisotropy level
                    GL.GetFloat(GetPName.MaxTextureMaxAnisotropy, out float maxAnisotropy);

                    // Use a reasonable anisotropy level (clamp to 16.0f for performance)
                    float anisotropy = Math.Min(maxAnisotropy, 16.0f);

                    GL.TexParameter(GLEnum.Texture2DArray, (TextureParameterName)0x84FE, anisotropy); // GL_TEXTURE_MAX_ANISOTROPY_EXT
                    GLHelpers.CheckErrors();
                }
            }
            catch {
                
            }
            */
        }

        /// <inheritdoc />
        public void Bind(int slot = 0) {
            GL.ActiveTexture(GLEnum.Texture0 + slot);
            GLHelpers.CheckErrors();
            GL.BindTexture(GLEnum.Texture2DArray, (uint)NativePtr);
            GLHelpers.CheckErrors();

            // Regenerate mipmaps if needed (deferred)
            if (_needsMipmapRegeneration) {
                GL.GenerateMipmap(GLEnum.Texture2DArray);
                GLHelpers.CheckErrors();
                _needsMipmapRegeneration = false;
            }
        }

        /// <inheritdoc />
        public unsafe int AddLayer(byte[] data) {
            Bind();

            for (int i = 0; i < _usedLayers.Length; i++) {
                if (!_usedLayers[i]) {
                    UpdateLayerInternal(i, data);
                    _usedLayers[i] = true;
                    return i;
                }
            }

            throw new InvalidOperationException("No free layers available in texture array.");
        }

        /// <inheritdoc />
        public void UpdateLayer(int layer, byte[] data) {
            Bind();
            UpdateLayerInternal(layer, data);
        }

        private void UpdateLayerInternal(int layer, byte[] data) {
            unsafe {
                GCHandle pinnedArray = GCHandle.Alloc(data, GCHandleType.Pinned);
                try {
                    var pixelsPtr = (void*)pinnedArray.AddrOfPinnedObject();
                    GL.TexSubImage3D(GLEnum.Texture2DArray, 0, 0, 0, layer, (uint)Width, (uint)Height, 1,
                        Format.ToPixelFormat(), PixelType.UnsignedByte, pixelsPtr);
                    GLHelpers.CheckErrors();

                    // Mark that we need mipmap regeneration, but don't do it immediately
                    _needsMipmapRegeneration = true;
                }
                finally {
                    pinnedArray.Free();
                }
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

        /// <summary>
        /// Force immediate mipmap regeneration for all layers
        /// </summary>
        public void RegenerateMipmaps() {
            Bind();
            GL.GenerateMipmap(GLEnum.Texture2DArray);
            GLHelpers.CheckErrors();
            _needsMipmapRegeneration = false;
        }

        /// <inheritdoc />
        public void Unbind() {
            // Implementation if needed
        }

        /// <inheritdoc />
        public void Dispose() {
            GL.DeleteTexture((uint)NativePtr);
        }
    }
}