using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using Chorizite.OpenGLSDLBackend.Extensions;
using Chorizite.OpenGLSDLBackend.Lib;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using System.Runtime.InteropServices;

namespace Chorizite.OpenGLSDLBackend {
    public class ManagedGLTextureArray : ITextureArray {
        private readonly bool[] _usedLayers;
        private readonly GL GL;
        private readonly ILogger _logger;
        private static int _nextId = 0;
        private bool _needsMipmapRegeneration = false;
        private readonly bool _isCompressed;
        private int _mipmapDirtyCount = 0;
        private readonly object _mipmapLock = new object();

        public int Slot { get; } = _nextId++;
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Size { get; private set; }
        public TextureFormat Format { get; private set; }
        public nint NativePtr { get; private set; }

        public ManagedGLTextureArray(OpenGLGraphicsDevice graphicsDevice, TextureFormat format, int width, int height,
            int size, ILogger logger) {
            if (width <= 0 || height <= 0 || size <= 0) {
                throw new ArgumentException($"Invalid texture array dimensions: {width}x{height}x{size}");
            }

            Format = format;
            Width = width;
            Height = height;
            Size = size;
            _usedLayers = new bool[size];
            GL = graphicsDevice.GL;
            _logger = logger;
            _isCompressed = IsCompressedFormat(format);
            GLHelpers.CheckErrors();

            NativePtr = (nint)GL.GenTexture();
            if (NativePtr == 0) {
                throw new InvalidOperationException("Failed to generate texture array.");
            }

            GLHelpers.CheckErrors();

            GL.BindTexture(GLEnum.Texture2DArray, (uint)NativePtr);
            GLHelpers.CheckErrors();

            int maxDimension = Math.Max(width, height);
            int mipLevels = (int)Math.Floor(Math.Log2(maxDimension)) + 1;

            GL.TexStorage3D(GLEnum.Texture2DArray, (uint)mipLevels, format.ToGL(), (uint)width, (uint)height,
                (uint)size);
            GLHelpers.CheckErrorsWithContext(
                $"Creating texture array storage (Format={format}, Size={width}x{height}x{size}, MipLevels={mipLevels})");

            if (_isCompressed) {
                GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureMinFilter,
                    (int)TextureMinFilter.Linear);
                GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureMaxLevel,
                    0); // No mips for compressed
            }
            else {
                GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureMinFilter,
                    (int)TextureMinFilter.LinearMipmapLinear);
                GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureMaxLevel, (int)mipLevels - 1);
            }

            GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.MirroredRepeat);
            GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.MirroredRepeat);

            // Set texture swizzle for single-channel formats
            if (format == TextureFormat.A8) {
                GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureSwizzleR, (int)GLEnum.One);
                GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureSwizzleG, (int)GLEnum.One);
                GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureSwizzleB, (int)GLEnum.One);
                GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureSwizzleA, (int)GLEnum.Red);
            }

            GLHelpers.CheckErrors();

            GpuMemoryTracker.TrackAllocation(CalculateTotalSize());
        }

        private long CalculateTotalSize() {
            int maxDimension = Math.Max(Width, Height);
            int mipLevels = _isCompressed ? 1 : (int)Math.Floor(Math.Log2(maxDimension)) + 1;
            long layerSize = GetExpectedDataSize();
            long totalSize = 0;

            for (int i = 0; i < mipLevels; i++) {
                int w = Math.Max(1, Width >> i);
                int h = Math.Max(1, Height >> i);
                if (_isCompressed) {
                    totalSize += TextureHelpers.GetCompressedLayerSize(w, h, Format) * Size;
                }
                else {
                    totalSize += (long)w * h * (layerSize / (Width * Height)) * Size;
                }
            }
            return totalSize;
        }

        private bool IsCompressedFormat(TextureFormat format) {
            return format == TextureFormat.DXT1 ||
                   format == TextureFormat.DXT3 ||
                   format == TextureFormat.DXT5;
        }

        public void Bind(int slot = 0) {
            if (NativePtr == 0) {
                throw new InvalidOperationException(
                    $"Cannot bind texture array: NativePtr is invalid (Slot={Slot}, Size={Width}x{Height}x{Size}).");
            }

            if (slot == 0) {
                BaseObjectRenderManager.CurrentAtlas = 0;
            }

            GL.BindSampler((uint)slot, 0);
            GL.ActiveTexture(GLEnum.Texture0 + slot);
            GLHelpers.CheckErrors();
            GL.BindTexture(GLEnum.Texture2DArray, (uint)NativePtr);
            GLHelpers.CheckErrors();

            if (!_isCompressed && _needsMipmapRegeneration) {
                lock (_mipmapLock) {
                    if (_mipmapDirtyCount > 0) {
                        try {
                            GL.GenerateMipmap(GLEnum.Texture2DArray);
                            GLHelpers.CheckErrorsWithContext("Generating mipmaps for texture array");
                            _mipmapDirtyCount = 0;
                            _needsMipmapRegeneration = false;
                        }
                        catch (Exception ex) {
                            _logger.LogWarning(ex, "Failed to generate mipmaps for texture array (Slot={Slot}). This is expected if the texture is currently being updated.", Slot);
                        }
                    }
                    else if (_mipmapDirtyCount == 0) {
                        _needsMipmapRegeneration = false;
                    }
                }
            }
        }

        public unsafe int AddLayer(byte[] data) {
            return AddLayer(data, null, null);
        }

        public unsafe int AddLayer(byte[] data, PixelFormat? uploadPixelFormat, PixelType? uploadPixelType) {
            for (int i = 0; i < _usedLayers.Length; i++) {
                if (!_usedLayers[i]) {
                    UpdateLayerInternal(i, data, uploadPixelFormat, uploadPixelType);
                    _usedLayers[i] = true;
                    return i;
                }
            }

            throw new InvalidOperationException(
                $"No free layers available in texture array (Slot={Slot}, Size={Width}x{Height}x{Size}).");
        }

        public unsafe int AddLayer(Span<byte> data) {
            return AddLayer(data.ToArray());
        }

        public void UpdateLayer(int layer, byte[] data) {
            UpdateLayer(layer, data, null, null);
        }

        public void UpdateLayer(int layer, byte[] data, PixelFormat? uploadPixelFormat, PixelType? uploadPixelType) {
            UpdateLayerInternal(layer, data, uploadPixelFormat, uploadPixelType);
            _usedLayers[layer] = true;
        }

        private unsafe void UpdateLayerInternal(int layer, byte[] data, PixelFormat? uploadPixelFormat,
            PixelType? uploadPixelType) {
            if (NativePtr == 0) {
                throw new InvalidOperationException("Texture array not created.");
            }

            // Ensure the texture is bound before uploading
            BaseObjectRenderManager.CurrentAtlas = 0;
            GL.BindTexture(GLEnum.Texture2DArray, (uint)NativePtr);
            GLHelpers.CheckErrors();

            if (layer < 0 || layer >= Size) {
                throw new ArgumentOutOfRangeException(nameof(layer),
                    $"Layer index {layer} is out of range [0, {Size - 1}] (Slot={Slot}).");
            }

            GCHandle pinnedArray = GCHandle.Alloc(data, GCHandleType.Pinned);
            try {
                var pixelsPtr = (void*)pinnedArray.AddrOfPinnedObject();

                if (_isCompressed) {
                    var internalFormat = Format.ToCompressedGL();
                    GL.CompressedTexSubImage3D(GLEnum.Texture2DArray, 0, 0, 0, layer,
                        (uint)Width, (uint)Height, 1, internalFormat, (uint)data.Length, pixelsPtr);
                }
                else {
                    var pixelFormat = uploadPixelFormat ?? Format.ToPixelFormat();
                    var pixelType = uploadPixelType ?? Format.ToPixelType();
                    GL.TexSubImage3D(GLEnum.Texture2DArray, 0, 0, 0, layer, (uint)Width, (uint)Height, 1,
                        pixelFormat, pixelType, pixelsPtr);
                }

                GLHelpers.CheckErrorsWithContext(
                    $"Uploading layer {layer} for {Format} {Width}x{Height} (Compressed={_isCompressed}) {uploadPixelFormat} // {uploadPixelType} (Slot={Slot})");
                _needsMipmapRegeneration = true;

                if (!_isCompressed) {
                    lock (_mipmapLock) {
                        _mipmapDirtyCount++;
                    }
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex,
                    "Error uploading texture layer {Layer} for {Width}x{Height} texture array (Slot={Slot})", layer,
                    Width, Height, Slot);
                throw;
            }
            finally {
                pinnedArray.Free();
            }
        }

        private void ClearLayerForMipmap(int layer) {
            // Upload a single black/transparent pixel to make layer defined
            byte[] clearData = new byte[GetExpectedDataSize()];
            Array.Clear(clearData, 0, clearData.Length); // Zero-fill (black/transparent)
            UpdateLayerInternal(layer, clearData, null, null);
        }

        private int GetExpectedDataSize() {
            if (_isCompressed) {
                return TextureHelpers.GetCompressedLayerSize(Width, Height, Format);
            }

            return Format switch {
                TextureFormat.RGBA8 => Width * Height * 4,
                TextureFormat.RGB8 => Width * Height * 3,
                TextureFormat.A8 => Width * Height * 1,
                TextureFormat.Rgba32f => Width * Height * 16,
                _ => throw new NotSupportedException($"Unsupported format {Format}")
            };
        }

        public void RemoveLayer(int layer) {
            if (layer < 0 || layer >= Size) {
                throw new ArgumentOutOfRangeException(nameof(layer),
                    $"Layer index {layer} is out of range [0, {Size - 1}] (Slot={Slot}).");
            }

            if (!_usedLayers[layer]) {
                throw new InvalidOperationException($"Layer {layer} is already free (Slot={Slot}).");
            }

            _usedLayers[layer] = false;

            // Make layer defined for mipmap completeness (uncompressed only)
            if (!_isCompressed) {
                ClearLayerForMipmap(layer);
                lock (_mipmapLock) {
                    _mipmapDirtyCount++; // Mark dirty to regen
                }
            }
        }

        public bool IsLayerUsed(int layer) {
            if (layer < 0 || layer >= Size) return false;
            return _usedLayers[layer];
        }

        public int GetUsedLayerCount() {
            return _usedLayers.Count(x => x);
        }

        public void Unbind() {
            GL.BindTexture(GLEnum.Texture2DArray, 0);
            GLHelpers.CheckErrors();
        }

        public void Dispose() {
            if (NativePtr != 0) {
                GL.DeleteTexture((uint)NativePtr);
                GLHelpers.CheckErrors();
                GpuMemoryTracker.TrackDeallocation(CalculateTotalSize());
                NativePtr = 0;
            }
        }
    }
}