using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using Chorizite.OpenGLSDLBackend;
using DatReaderWriter.Enums;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using PixelFormat = Silk.NET.OpenGL.PixelFormat;

namespace WorldBuilder.Editors.Landscape {
    public class TextureAtlasManager : IDisposable {
        private readonly OpenGLRenderer _renderer;
        private readonly int _textureWidth;
        private readonly int _textureHeight;
        private readonly TextureFormat _format;
        private readonly Dictionary<TextureKey, int> _textureIndices = new();
        private readonly Dictionary<int, int> _refCounts = new();
        private readonly Stack<int> _freeSlots = new();
        private int _nextIndex = 0;
        private const int InitialCapacity = 90;

        public ManagedGLTextureArray TextureArray { get; private set; }
        public int UsedSlots => _textureIndices.Count;
        public int TotalSlots => (TextureArray as ManagedGLTextureArray)?.Size ?? InitialCapacity;
        public int FreeSlots => TotalSlots - UsedSlots;

        public TextureAtlasManager(OpenGLRenderer renderer, int width, int height, TextureFormat format = TextureFormat.RGBA8) {
            _renderer = renderer;
            _textureWidth = width;
            _textureHeight = height;
            _format = format;
            TextureArray = renderer.GraphicsDevice.CreateTextureArray(format, width, height, InitialCapacity) as ManagedGLTextureArray;
        }

        public int AddTexture(TextureKey surfaceId, byte[] data, PixelFormat? uploadPixelFormat = null, PixelType? uploadPixelType = null) {
            if (_textureIndices.TryGetValue(surfaceId, out var existingIndex)) {
                _refCounts[existingIndex]++;
                return existingIndex;
            }

            int index;
            if (_freeSlots.Count > 0) {
                index = _freeSlots.Pop();
            }
            else {
                index = _nextIndex++;
                var managedArray = TextureArray as ManagedGLTextureArray;
                if (managedArray != null && index >= managedArray.Size) {
                    throw new Exception($"Texture atlas is full!. {managedArray.Size} / {_nextIndex} used.");
                    /*
                    // This is not implemented yet
                    // Grow the array
                    int newSize = managedArray.Size * 2;
                    var newArray = _renderer.GraphicsDevice.CreateTextureArray(_format, _textureWidth, _textureHeight, newSize);

                    // Copy existing textures to new array
                    for (int i = 0; i < managedArray.Size; i++) {
                        if (_refCounts.ContainsKey(i)) {
                            byte[] layerData = managedArray.GetLayerData(i); // Assume this method exists to download layer data
                            newArray.UpdateLayer(i, layerData);
                        }
                    }

                    managedArray.Dispose();
                    TextureArray = newArray;
                    */
                }
            }

            try {
                TextureArray.UpdateLayer(index, data, uploadPixelFormat, uploadPixelType);
                _textureIndices[surfaceId] = index;
                _refCounts[index] = 1;
                return index;
            }
            catch (Exception ex) {
                Console.WriteLine($"Error adding texture to atlas: {ex.Message}");
                if (!_textureIndices.ContainsKey(surfaceId)) {
                    _freeSlots.Push(index);
                }
                throw;
            }
        }

        public void ReleaseTexture(TextureKey surfaceId) {
            if (!_textureIndices.TryGetValue(surfaceId, out var index)) return;

            if (!_refCounts.ContainsKey(index)) {
                Console.WriteLine($"Warning: Releasing texture {surfaceId} at index {index} with no ref count");
                return;
            }

            _refCounts[index]--;
            if (_refCounts[index] <= 0) {
                _textureIndices.Remove(surfaceId);
                _refCounts.Remove(index);
                _freeSlots.Push(index);
                var managedArray = TextureArray as ManagedGLTextureArray;
                managedArray?.RemoveLayer(index);
            }
        }

        public bool HasTexture(TextureKey surfaceId) {
            return _textureIndices.ContainsKey(surfaceId);
        }

        public int GetTextureIndex(TextureKey surfaceId) {
            return _textureIndices.TryGetValue(surfaceId, out var index) ? index : -1;
        }

        public void Dispose() {
            TextureArray?.Dispose();
            _textureIndices.Clear();
            _refCounts.Clear();
            _freeSlots.Clear();
        }
        public struct TextureKey {
            public uint SurfaceId;
            public uint PaletteId;
            public StipplingType Stippling;
            public bool IsSolid;

            public override bool Equals(object obj) {
                if (obj is TextureKey other) {
                    return SurfaceId == other.SurfaceId &&
                           PaletteId == other.PaletteId &&
                           Stippling == other.Stippling &&
                           IsSolid == other.IsSolid;
                }
                return false;
            }

            public override int GetHashCode() {
                return HashCode.Combine(SurfaceId, PaletteId, Stippling, IsSolid);
            }
        }
    }
}