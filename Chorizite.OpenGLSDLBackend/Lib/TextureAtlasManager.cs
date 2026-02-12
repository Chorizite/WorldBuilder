using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using DatReaderWriter.Enums;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using PixelFormat = Silk.NET.OpenGL.PixelFormat;

namespace Chorizite.OpenGLSDLBackend.Lib {
    /// <summary>
    /// Manages texture arrays grouped by (Width, Height, Format).
    /// Deduplicates textures by a TextureKey and supports reference counting.
    /// </summary>
    public class TextureAtlasManager : IDisposable {
        private readonly OpenGLGraphicsDevice _graphicsDevice;
        private readonly int _textureWidth;
        private readonly int _textureHeight;
        private readonly TextureFormat _format;
        private readonly Dictionary<TextureKey, int> _textureIndices = new();
        private readonly Dictionary<int, int> _refCounts = new();
        private readonly Stack<int> _freeSlots = new();
        private int _nextIndex = 0;
        private const int InitialCapacity = 90;

        public ManagedGLTextureArray TextureArray { get; private set; } = null!;
        public int UsedSlots => _textureIndices.Count;
        public int TotalSlots => TextureArray?.Size ?? InitialCapacity;
        public int FreeSlots => TotalSlots - UsedSlots;

        public TextureAtlasManager(OpenGLGraphicsDevice graphicsDevice, int width, int height, TextureFormat format = TextureFormat.RGBA8) {
            _graphicsDevice = graphicsDevice;
            _textureWidth = width;
            _textureHeight = height;
            _format = format;
            TextureArray = (ManagedGLTextureArray)graphicsDevice.CreateTextureArrayInternal(format, width, height, InitialCapacity);
        }

        public int AddTexture(TextureKey key, byte[] data, PixelFormat? uploadPixelFormat = null, PixelType? uploadPixelType = null) {
            if (_textureIndices.TryGetValue(key, out var existingIndex)) {
                _refCounts[existingIndex]++;
                return existingIndex;
            }

            int index;
            if (_freeSlots.Count > 0) {
                index = _freeSlots.Pop();
            }
            else {
                index = _nextIndex++;
                if (index >= TextureArray.Size) {
                    throw new Exception($"Texture atlas is full! {TextureArray.Size} / {_nextIndex} used.");
                }
            }

            try {
                TextureArray.UpdateLayer(index, data, uploadPixelFormat, uploadPixelType);
                _textureIndices[key] = index;
                _refCounts[index] = 1;
                return index;
            }
            catch (Exception) {
                if (!_textureIndices.ContainsKey(key)) {
                    _freeSlots.Push(index);
                }
                throw;
            }
        }

        public void ReleaseTexture(TextureKey key) {
            if (!_textureIndices.TryGetValue(key, out var index)) return;

            if (!_refCounts.ContainsKey(index)) return;

            _refCounts[index]--;
            if (_refCounts[index] <= 0) {
                _textureIndices.Remove(key);
                _refCounts.Remove(index);
                _freeSlots.Push(index);
                TextureArray?.RemoveLayer(index);
            }
        }

        public bool HasTexture(TextureKey key) => _textureIndices.ContainsKey(key);

        public int GetTextureIndex(TextureKey key) =>
            _textureIndices.TryGetValue(key, out var index) ? index : -1;

        public void Dispose() {
            TextureArray?.Dispose();
            _textureIndices.Clear();
            _refCounts.Clear();
            _freeSlots.Clear();
        }

        public struct TextureKey : IEquatable<TextureKey> {
            public uint SurfaceId;
            public uint PaletteId;
            public StipplingType Stippling;
            public bool IsSolid;

            public bool Equals(TextureKey other) {
                return SurfaceId == other.SurfaceId &&
                       PaletteId == other.PaletteId &&
                       Stippling == other.Stippling &&
                       IsSolid == other.IsSolid;
            }

            public override bool Equals(object? obj) {
                return obj is TextureKey other && Equals(other);
            }

            public override int GetHashCode() {
                return HashCode.Combine(SurfaceId, PaletteId, Stippling, IsSolid);
            }
        }
    }
}
