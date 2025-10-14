// TextureAtlasManager.cs - Simplified with texture reuse and proper cleanup
using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using Chorizite.OpenGLSDLBackend;
using System;
using System.Collections.Generic;

namespace WorldBuilder.Editors.Landscape {
    public class TextureAtlasManager : IDisposable {
        private readonly OpenGLRenderer _renderer;
        private readonly int _textureWidth;
        private readonly int _textureHeight;
        private readonly Dictionary<uint, int> _textureIndices = new();
        private readonly Dictionary<int, int> _refCounts = new();
        private readonly Stack<int> _freeSlots = new();
        private int _nextIndex = 0;
        private const int InitialCapacity = 90;

        public ITextureArray TextureArray { get; private set; }

        public TextureAtlasManager(OpenGLRenderer renderer, int width, int height) {
            _renderer = renderer;
            _textureWidth = width;
            _textureHeight = height;
            TextureArray = renderer.GraphicsDevice.CreateTextureArray(TextureFormat.RGBA8, width, height, InitialCapacity);
        }

        public int AddTexture(uint surfaceId, byte[] data) {
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
                if (index >= managedArray?.Size) {
                    int newSize = managedArray.Size * 2;
                    var newArray = _renderer.GraphicsDevice.CreateTextureArray(TextureFormat.RGBA8, _textureWidth, _textureHeight, newSize);
                    TextureArray = newArray;
                }
            }

            TextureArray.UpdateLayer(index, data);
            _textureIndices[surfaceId] = index;
            _refCounts[index] = 1;
            return index;
        }

        public void ReleaseTexture(uint surfaceId) {
            if (!_textureIndices.TryGetValue(surfaceId, out var index)) return;

            _refCounts[index]--;
            if (_refCounts[index] <= 0) {
                _textureIndices.Remove(surfaceId);
                _refCounts.Remove(index);
                _freeSlots.Push(index);
            }
        }

        public void Dispose() {
            TextureArray?.Dispose();
        }
    }
}