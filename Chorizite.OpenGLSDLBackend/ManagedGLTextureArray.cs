using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using Chorizite.OpenGLSDLBackend.Extensions;
using Silk.NET.OpenGL;
using System.Runtime.InteropServices;

namespace Chorizite.OpenGLSDLBackend {
    public class ManagedGLTextureArray : ITextureArray {
        private readonly bool[] _usedLayers;
        private readonly GL GL;
        private static int _nextId = 0;
        private bool _needsMipmapRegeneration = false;

        public int Slot { get; } = _nextId++;
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Size { get; private set; }
        public TextureFormat Format { get; private set; }
        public nint NativePtr { get; private set; }

        public ManagedGLTextureArray(OpenGLGraphicsDevice graphicsDevice, TextureFormat format, int width, int height, int size) {
            if (width <= 0 || height <= 0 || size <= 0) {
                throw new ArgumentException($"Invalid texture array dimensions: {width}x{height}x{size}");
            }
            Format = format;
            Width = width;
            Height = height;
            Size = size;
            _usedLayers = new bool[size];
            GL = graphicsDevice.GL;

            NativePtr = (nint)GL.GenTexture();
            if (NativePtr == 0) {
                throw new InvalidOperationException("Failed to generate texture array.");
            }
            GLHelpers.CheckErrors();

            GL.BindTexture(GLEnum.Texture2DArray, (uint)NativePtr);
            GLHelpers.CheckErrors();

            int maxDimension = Math.Max(width, height);
            int mipLevels = (int)Math.Floor(Math.Log2(maxDimension)) + 1;

            GL.TexStorage3D(GLEnum.Texture2DArray, (uint)mipLevels, format.ToGL(), (uint)width, (uint)height, (uint)size);
            GLHelpers.CheckErrors();

            GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GLHelpers.CheckErrors();
        }

        public void Bind(int slot = 0) {
            if (NativePtr == 0) {
                throw new InvalidOperationException($"Cannot bind texture array: NativePtr is invalid (Slot={Slot}, Size={Width}x{Height}x{Size}).");
            }
            GL.BindSampler((uint)slot, 0);
            GL.ActiveTexture(GLEnum.Texture0 + slot);
            GLHelpers.CheckErrors();
            GL.BindTexture(GLEnum.Texture2DArray, (uint)NativePtr);
            GLHelpers.CheckErrors();

            if (_needsMipmapRegeneration) {
                GL.GenerateMipmap(GLEnum.Texture2DArray);
                GLHelpers.CheckErrors();
                _needsMipmapRegeneration = false;
            }
        }

        public unsafe int AddLayer(byte[] data) {
            Bind();
            for (int i = 0; i < _usedLayers.Length; i++) {
                if (!_usedLayers[i]) {
                    UpdateLayerInternal(i, data);
                    _usedLayers[i] = true;
                    return i;
                }
            }
            throw new InvalidOperationException($"No free layers available in texture array (Slot={Slot}, Size={Width}x{Height}x{Size}).");
        }

        public unsafe int AddLayer(Span<byte> data) {
            return AddLayer(data.ToArray());
        }

        public void UpdateLayer(int layer, byte[] data) {
            Bind();
            UpdateLayerInternal(layer, data);
        }

        private void UpdateLayerInternal(int layer, byte[] data) {
            unsafe {
                if (layer < 0 || layer >= Size) {
                    throw new ArgumentOutOfRangeException(nameof(layer), $"Layer index {layer} is out of range [0, {Size - 1}] (Slot={Slot}).");
                }
                if (data == null || data.Length != Width * Height * 4) {
                    throw new ArgumentException($"Data buffer size ({data?.Length ?? 0}) does not match expected size ({Width * Height * 4}) for RGBA8 format (Slot={Slot}).");
                }

                GLHelpers.CheckErrors();
                GCHandle pinnedArray = GCHandle.Alloc(data, GCHandleType.Pinned);
                try {
                    var pixelsPtr = (void*)pinnedArray.AddrOfPinnedObject();
                    GL.TexSubImage3D(GLEnum.Texture2DArray, 0, 0, 0, layer, (uint)Width, (uint)Height, 1,
                        Format.ToPixelFormat(), PixelType.UnsignedByte, pixelsPtr);
                    GLHelpers.CheckErrors();
                    _needsMipmapRegeneration = true;
                }
                catch (Exception ex) {
                    Console.WriteLine($"Error uploading texture layer {layer} for {Width}x{Height} texture array (Slot={Slot}): {ex.Message}");
                    throw;
                }
                finally {
                    pinnedArray.Free();
                }
            }
        }

        public void RemoveLayer(int layer) {
            if (layer < 0 || layer >= Size) {
                throw new ArgumentOutOfRangeException(nameof(layer), $"Layer index {layer} is out of range [0, {Size - 1}] (Slot={Slot}).");
            }
            if (!_usedLayers[layer]) {
                throw new InvalidOperationException($"Layer {layer} is already free (Slot={Slot}).");
            }
            _usedLayers[layer] = false;
        }

        public void Unbind() {
            GL.BindTexture(GLEnum.Texture2DArray, 0);
            GLHelpers.CheckErrors();
        }

        public void Dispose() {
            if (NativePtr != 0) {
                GL.DeleteTexture((uint)NativePtr);
                GLHelpers.CheckErrors();
                NativePtr = 0;
            }
        }
    }
}