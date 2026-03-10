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
        private readonly OpenGLGraphicsDevice _device;
        private readonly ILogger _logger;
        private static int _nextId = 0;
        private bool _needsMipmapRegeneration = false;
        private readonly bool _isCompressed;
        private int _mipmapDirtyCount = 0;
        private readonly object _mipmapLock = new object();
        private uint _pboId;
        private int _pboSize;
        private readonly List<TextureLayerUpdate> _pendingUpdates = new();

        private struct TextureLayerUpdate {
            public int Layer;
            public int Offset;
            public int Size;
            public PixelFormat? UploadPixelFormat;
            public PixelType? UploadPixelType;
        }

        public int Slot { get; } = _nextId++;
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Size { get; private set; }
        public TextureFormat Format { get; private set; }
        public nint NativePtr { get; private set; }
        public ulong BindlessHandle { get; private set; }
        public ulong BindlessWrapHandle { get; private set; }
        public ulong BindlessClampHandle { get; private set; }
        public long TotalSizeInBytes => CalculateTotalSize();

        public ManagedGLTextureArray(OpenGLGraphicsDevice graphicsDevice, TextureFormat format, int width, int height,
            int size, ILogger logger, TextureParameters? texParams = null) {
            var p = texParams ?? TextureParameters.Default;
            if (width <= 0 || height <= 0 || size <= 0) {
                throw new ArgumentException($"Invalid texture array dimensions: {width}x{height}x{size}");
            }

            Format = format;
            Width = width;
            Height = height;
            Size = size;
            _usedLayers = new bool[size];
            _device = graphicsDevice;
            GL = graphicsDevice.GL;
            _logger = logger;
            _isCompressed = IsCompressedFormat(format);
            GLHelpers.CheckErrors(GL);

            NativePtr = (nint)GL.GenTexture();
            if (NativePtr == 0) {
                throw new InvalidOperationException("Failed to generate texture array.");
            }
            GpuMemoryTracker.TrackResourceAllocation(GpuResourceType.Texture);

            GLHelpers.CheckErrors(GL);

            GL.BindTexture(GLEnum.Texture2DArray, (uint)NativePtr);
            GLHelpers.CheckErrors(GL);

            int maxDimension = Math.Max(width, height);
            int mipLevels = (int)Math.Floor(Math.Log2(maxDimension)) + 1;

            GL.TexStorage3D(GLEnum.Texture2DArray, (uint)mipLevels, format.ToGL(), (uint)width, (uint)height,
                (uint)size);
            GLHelpers.CheckErrorsWithContext(GL,
                $"Creating texture array storage (Format={format}, Size={width}x{height}x{size}, MipLevels={mipLevels})");

            GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureMinFilter,
                (int)p.MinFilter);
            GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureMaxLevel, (int)mipLevels - 1);

            GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureMagFilter, (int)p.MagFilter);

            GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureWrapS, (int)p.WrapS);
            GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureWrapT, (int)p.WrapT);

            if (p.EnableAnisotropicFiltering && graphicsDevice.RenderSettings.EnableAnisotropicFiltering) {
                float maxAnisotropy = 0f;
                GL.GetFloat(GLEnum.MaxTextureMaxAnisotropy, out maxAnisotropy);

                if (maxAnisotropy > 0) {
                    GL.TexParameter(GLEnum.Texture2DArray, GLEnum.TextureMaxAnisotropy, maxAnisotropy);
                }
            }

            // Set texture swizzle for single-channel formats
            if (format == TextureFormat.A8) {
                GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureSwizzleR, (int)GLEnum.One);
                GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureSwizzleG, (int)GLEnum.One);
                GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureSwizzleB, (int)GLEnum.One);
                GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureSwizzleA, (int)GLEnum.Red);
            }

            GLHelpers.CheckErrors(GL);

            GpuMemoryTracker.TrackAllocation(CalculateTotalSize(), GpuResourceType.Texture);

            if (_device.HasBindless && _device.BindlessExtension != null) {
                BindlessHandle = _device.BindlessExtension.GetTextureHandle((uint)NativePtr);
                BindlessWrapHandle = _device.BindlessExtension.GetTextureSamplerHandle((uint)NativePtr, _device.WrapSampler);
                BindlessClampHandle = _device.BindlessExtension.GetTextureSamplerHandle((uint)NativePtr, _device.ClampSampler);

                _device.BindlessExtension.MakeTextureHandleResident(BindlessHandle);
                _device.BindlessExtension.MakeTextureHandleResident(BindlessWrapHandle);
                _device.BindlessExtension.MakeTextureHandleResident(BindlessClampHandle);
            }

            _pboId = GL.GenBuffer();
            GpuMemoryTracker.TrackResourceAllocation(GpuResourceType.Buffer);
        }

        public long CalculateTotalSize() {
            int maxDimension = Math.Max(Width, Height);
            int mipLevels = (int)Math.Floor(Math.Log2(maxDimension)) + 1;
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
                return;
            }

            GL.GetInteger(GLEnum.ActiveTexture, out int oldActiveTexture);
            GLEnum targetTextureUnit = GLEnum.Texture0 + slot;
            bool changedUnit = (GLEnum)oldActiveTexture != targetTextureUnit;

            if (changedUnit) {
                GL.ActiveTexture(targetTextureUnit);
            }
            
            GL.BindSampler((uint)slot, 0);
            GL.BindTexture(GLEnum.Texture2DArray, (uint)NativePtr);
            
            if (changedUnit) {
                GL.ActiveTexture((GLEnum)oldActiveTexture);
            }
            GLHelpers.CheckErrors(GL);
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

            if (layer < 0 || layer >= Size) {
                throw new ArgumentOutOfRangeException(nameof(layer),
                    $"Layer index {layer} is out of range [0, {Size - 1}] (Slot={Slot}).");
            }

            int currentPboOffset = 0;
            lock (_mipmapLock) {
                if (_pendingUpdates.Count > 0) {
                    var lastUpdate = _pendingUpdates[^1];
                    currentPboOffset = lastUpdate.Offset + lastUpdate.Size;
                }

                // Align to 4 bytes for safety
                currentPboOffset = (currentPboOffset + 3) & ~3;

                if (currentPboOffset + data.Length > _pboSize) {
                    // Flush existing updates first because BufferData will orphan/clear the PBO
                    if (_pendingUpdates.Count > 0) {
                        ProcessDirtyUpdatesInternal();
                    }
                    currentPboOffset = 0;

                    int newSize = Math.Max(_pboSize * 2, data.Length);
                    newSize = Math.Max(newSize, GetExpectedDataSize() * 4); // Initial size 4 layers

                    GL.BindBuffer(GLEnum.PixelUnpackBuffer, _pboId);
                    GL.BufferData(GLEnum.PixelUnpackBuffer, (nuint)newSize, (void*)0, GLEnum.StreamDraw);
                    
                    if (_pboSize > 0) {
                        GpuMemoryTracker.TrackDeallocation(_pboSize, GpuResourceType.Buffer);
                    }
                    _pboSize = newSize;
                    GpuMemoryTracker.TrackAllocation(_pboSize, GpuResourceType.Buffer);
                }
                else {
                    GL.BindBuffer(GLEnum.PixelUnpackBuffer, _pboId);
                }

                fixed (byte* ptr = data) {
                    GL.BufferSubData(GLEnum.PixelUnpackBuffer, (nint)currentPboOffset, (nuint)data.Length, ptr);
                }
                GL.BindBuffer(GLEnum.PixelUnpackBuffer, 0);

                _pendingUpdates.Add(new TextureLayerUpdate {
                    Layer = layer,
                    Offset = currentPboOffset,
                    Size = data.Length,
                    UploadPixelFormat = uploadPixelFormat,
                    UploadPixelType = uploadPixelType
                });

                _needsMipmapRegeneration = true;
                _mipmapDirtyCount++;
            }
        }

        public void ProcessDirtyUpdates() {
            lock (_mipmapLock) {
                ProcessDirtyUpdatesInternal();
            }
        }

        private unsafe void ProcessDirtyUpdatesInternal() {
            if (_pendingUpdates.Count == 0 && !_needsMipmapRegeneration) return;
            
            GLHelpers.CheckErrors(GL);

            GL.GetInteger(GLEnum.ActiveTexture, out int oldActiveTexture);
            BaseObjectRenderManager.CurrentAtlas = 0;
            
            GL.GetInteger(GLEnum.TextureBinding2DArray, out int oldBinding);
            GL.BindTexture(GLEnum.Texture2DArray, (uint)NativePtr);

            bool wasResident = false;
            if (BindlessHandle != 0 && _device.BindlessExtension != null && _device.BindlessExtension.IsTextureHandleResident(BindlessHandle)) {
                _device.BindlessExtension.MakeTextureHandleNonResident(BindlessHandle);
                wasResident = true;
            }

            if (_pendingUpdates.Count > 0) {
                GL.BindBuffer(GLEnum.PixelUnpackBuffer, _pboId);

                foreach (var update in _pendingUpdates) {
                    if (_isCompressed) {
                        var internalFormat = Format.ToCompressedGL();
                        GL.CompressedTexSubImage3D(GLEnum.Texture2DArray, 0, 0, 0, update.Layer,
                            (uint)Width, (uint)Height, 1, internalFormat, (uint)update.Size, (void*)update.Offset);
                    }
                    else {
                        var pixelFormat = update.UploadPixelFormat ?? Format.ToPixelFormat();
                        var pixelType = update.UploadPixelType ?? Format.ToPixelType();
                        GL.TexSubImage3D(GLEnum.Texture2DArray, 0, 0, 0, update.Layer, (uint)Width, (uint)Height, 1,
                            pixelFormat, pixelType, (void*)update.Offset);
                    }
                }

                GL.BindBuffer(GLEnum.PixelUnpackBuffer, 0);
                _pendingUpdates.Clear();
            }

            if (_needsMipmapRegeneration && _mipmapDirtyCount > 0) {
                if (_isCompressed) {
                    _logger.LogDebug("Skipping automatic mipmap generation for compressed texture array (Slot={Slot})", Slot);
                }
                else if (!GLHelpers.ValidateTextureMipmapStatus(GL, GLEnum.Texture2DArray, out var errorMessage)) {
                    _logger.LogWarning("Mipmap validation failed for texture array (Slot={Slot}): {Error}", Slot, errorMessage);
                }
                else {
                    try {
                        GL.GenerateMipmap(GLEnum.Texture2DArray);
                    }
                    catch (Exception ex) {
                        _logger.LogWarning(ex, "Failed to generate mipmaps for texture array (Slot={Slot}).", Slot);
                    }
                }
                _mipmapDirtyCount = 0;
                _needsMipmapRegeneration = false;
            }

            if (wasResident && BindlessHandle != 0 && _device.BindlessExtension != null) {
                _device.BindlessExtension.MakeTextureHandleResident(BindlessHandle);
            }

            GL.BindTexture(GLEnum.Texture2DArray, (uint)oldBinding);
            GL.ActiveTexture((GLEnum)oldActiveTexture);
            GLHelpers.CheckErrors(GL);
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
            }

            lock (_mipmapLock) {
                _mipmapDirtyCount++; // Mark dirty to regen
                _needsMipmapRegeneration = true;
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
            GLHelpers.CheckErrors(GL);
        }

        public void GenerateMipmaps() {
            _needsMipmapRegeneration = true;
            lock (_mipmapLock) {
                _mipmapDirtyCount++;
            }
        }

        public void Dispose() {
            if (BindlessHandle != 0 && _device.BindlessExtension != null) {
                _device.BindlessExtension.MakeTextureHandleNonResident(BindlessHandle);
                BindlessHandle = 0;
            }
            if (NativePtr != 0) {
                GL.DeleteTexture((uint)NativePtr);
                GLHelpers.CheckErrors(GL);
                GpuMemoryTracker.TrackDeallocation(CalculateTotalSize(), GpuResourceType.Texture);
                GpuMemoryTracker.TrackResourceDeallocation(GpuResourceType.Texture);
                NativePtr = 0;
            }
            if (_pboId != 0) {
                GL.DeleteBuffer(_pboId);
                GpuMemoryTracker.TrackResourceDeallocation(GpuResourceType.Buffer);
                if (_pboSize > 0) {
                    GpuMemoryTracker.TrackDeallocation(_pboSize, GpuResourceType.Buffer);
                }
                _pboId = 0;
            }
        }
    }
}