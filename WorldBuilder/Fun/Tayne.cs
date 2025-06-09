using System;
using System.Numerics;
using Raylib_cs;
using WorldBuilder.Lib;

namespace WorldBuilder.Fun {
    public class Tayne : IDisposable {
        private readonly Texture2D _texture;
        private readonly Image _image; // Single image containing all frames
        private readonly int _frameCount;
        private int _currentAnimFrame;
        private int _frameDelay; // Frames to wait before switching
        private int _frameCounter; // Counts frames for delay
        private readonly Vector3 _position;
        private readonly float _scale;
        private readonly int _frameWidth;
        private readonly int _frameHeight;
        private readonly int _frameSize; // Size of one frame in bytes (width * height * 4 for RGBA)

        private const int MaxFrameDelay = 60;
        private const int MinFrameDelay = 30;

        public Tayne(string gifPath, Vector3 position, float scale = 1.0f, int initialFrameDelay = 8) {
            _position = position;
            _scale = scale;
            _frameDelay = initialFrameDelay;
            _frameCounter = 0;
            _currentAnimFrame = 0;
            _scale = 10f;

            // Load GIF animation (all frames in one image)
            _image = Raylib.LoadImageAnim(gifPath, out _frameCount);
            if (_frameCount == 0) {
                throw new Exception("Failed to load GIF frames from " + gifPath);
            }

            // Validate image format (should be RGBA, 4 bytes per pixel)
            if (_image.Format != PixelFormat.UncompressedR8G8B8A8) {
                throw new Exception($"Unexpected image format: {_image.Format}. Expected RGBA.");
            }

            // Store frame dimensions
            _frameWidth = _image.Width;
            _frameHeight = _image.Height;
            _frameSize = _frameWidth * _frameHeight * 4; // RGBA = 4 bytes per pixel

            // Process image to make whitish pixels transparent
            MakeWhitishPixelsTransparent(ref _image);

            // Load initial texture
            _texture = Raylib.LoadTextureFromImage(_image);

            // Log frame information for debugging
            Console.WriteLine($"Tayne: Loaded {_frameCount} frames, width={_frameWidth}, height={_frameHeight}, frameSize={_frameSize}");
        }

        private unsafe void MakeWhitishPixelsTransparent(ref Image image) {
            const byte whiteThreshold = 150; // Threshold for "whitish" pixels

            // Get pixel data
            Color* pixels = (Color*)image.Data;
            int pixelCount = image.Width * image.Height * _frameCount;

            // Ensure pixel count is valid
            if (pixelCount <= 0 || image.Data == null) {
                throw new Exception("Invalid pixel count or null image data.");
            }

            // Modify pixels directly
            for (int i = 0; i < pixelCount; i++) {
                if (pixels[i].R > whiteThreshold && pixels[i].G > whiteThreshold && pixels[i].B > whiteThreshold) {
                    pixels[i].A = 0; // Set alpha to 0 for transparency
                }
            }

            // No need to update image data since we modified it directly
        }

        public void Update(float deltaTime) {
            // Handle frame delay control with keyboard
            if (Raylib.IsKeyPressed(KeyboardKey.Right)) {
                _frameDelay = Math.Min(_frameDelay + 1, MaxFrameDelay);
                Console.WriteLine($"Tayne: Frame delay increased to {_frameDelay}");
            }
            else if (Raylib.IsKeyPressed(KeyboardKey.Left)) {
                _frameDelay = Math.Max(_frameDelay - 1, MinFrameDelay);
                Console.WriteLine($"Tayne: Frame delay decreased to {_frameDelay}");
            }

            _frameDelay = 300;

            // Update animation frame
            _frameCounter++;
            if (_frameCounter >= _frameDelay) {
                _currentAnimFrame = (_currentAnimFrame + 1) % _frameCount;
                _frameCounter = 0;

                // Calculate offset for the current frame
                int dataOffset = _frameSize * _currentAnimFrame;

                // Validate offset
                if (dataOffset + _frameSize > _image.Width * _image.Height * 4 * _frameCount) {
                    Console.WriteLine($"Tayne: Invalid data offset {dataOffset} for frame {_currentAnimFrame}. Skipping texture update.");
                    return;
                }

                // Update texture with the current frame's data
                unsafe {
                    byte* dataPtr = (byte*)_image.Data;
                    if (dataPtr == null) {
                        Console.WriteLine("Tayne: Image data pointer is null. Skipping texture update.");
                        return;
                    }
                    Raylib.UpdateTexture(_texture, dataPtr + dataOffset);
                }

                //Console.WriteLine($"Tayne: Updated texture to frame {_currentAnimFrame}, offset={dataOffset}");
            }
        }

        public void Render(Camera3D camera) {
            // Draw as a billboard in 3D space
            Raylib.DrawBillboard(camera, _texture, _position, _scale, Color.White);
        }

        public void Dispose() {
            Raylib.UnloadTexture(_texture);
            Raylib.UnloadImage(_image);
        }
    }
}