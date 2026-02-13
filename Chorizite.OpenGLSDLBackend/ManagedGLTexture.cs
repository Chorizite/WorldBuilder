using Chorizite.Core.Dats;
using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using Silk.NET.OpenGL;
using Image = SixLabors.ImageSharp.Image;

namespace Chorizite.OpenGLSDLBackend {
    public unsafe class ManagedGLTexture : ITexture {
        private uint _texture;
        private readonly OpenGLGraphicsDevice _device;

        private GL GL => (_device as OpenGLGraphicsDevice).GL;

        /// <inheritdoc/>
        public IntPtr NativePtr => (IntPtr)_texture;

        /// <inheritdoc/>
        public int Width { get; private set; }

        /// <inheritdoc/>
        public int Height { get; private set; }

        public TextureFormat Format => TextureFormat.RGBA8;

        /// <inheritdoc/>
        public ManagedGLTexture(OpenGLGraphicsDevice device, byte[]? source, int width, int height) {
            _device = device;
            _texture = GL.GenTexture();
            Width = width;
            Height = height;
            GL.BindTexture(GLEnum.Texture2D, _texture);
            GLHelpers.CheckErrors();

            // Get the pixel data from the ImageSharp bitmap
            byte[] pixelData = new byte[width * height * 4];


            fixed (byte* data = &pixelData[0]) {
                GL.TexImage2D(GLEnum.Texture2D, 0, (int)InternalFormat.Rgba8, (uint)width, (uint)height, 0, PixelFormat.Rgba, (PixelType)0x1401, data);
                GLHelpers.CheckErrors();
            }
            GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(GLEnum.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GLHelpers.CheckErrors();
            //  GL.TexParameterI(GLEnum.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapNearest);
            // GL.TexParameterI(GLEnum.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

            //  GLHelpers.CheckErrors();

            //  GL.TexParameterI(GLEnum.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            // GL.TexParameterI(GLEnum.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            //  GLHelpers.CheckErrors();

            GL.GenerateMipmap(GLEnum.Texture2D);
            GLHelpers.CheckErrors();
            GL.BindTexture(GLEnum.Texture2D, 0);
            GLHelpers.CheckErrors();

            GpuMemoryTracker.TrackAllocation(Width * Height * 4);
        }

        /// <inheritdoc/>
        public ManagedGLTexture(OpenGLGraphicsDevice device, string file) {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        protected ManagedGLTexture(OpenGLGraphicsDevice device, Image bitmap) {
            throw new NotImplementedException();
        }
       
        public void SetData(Rectangle rectangle, byte[] data) {
            if (_texture == 0) return;

            GL.BindTexture(GLEnum.Texture2D, _texture);
            GLHelpers.CheckErrors();

            fixed (byte* ptr = data) {
                GL.TexSubImage2D(
                    GLEnum.Texture2D,
                    0, // level
                    rectangle.X,
                    rectangle.Y,
                    (uint)rectangle.Width,
                    (uint)rectangle.Height,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    ptr
                );
                GLHelpers.CheckErrors();
            }

            // Generate mipmaps if needed
            GL.GenerateMipmap(GLEnum.Texture2D);
            GLHelpers.CheckErrors();

            GL.BindTexture(GLEnum.Texture2D, 0);
            GLHelpers.CheckErrors();
        }

        public void Bind(int slot = 0) {
            GL.BindSampler((uint)slot, 0);
            GL.ActiveTexture(GLEnum.Texture0 + slot);
            GLHelpers.CheckErrors();
            GL.BindTexture(GLEnum.Texture2D, (uint)NativePtr);
            GLHelpers.CheckErrors();
        }

        public void Unbind() {
            GL.BindTexture(GLEnum.Texture2D, 0);
            GLHelpers.CheckErrors();
        }

        protected void ReleaseTexture() {
            if (_texture != 0) {
                GL.DeleteTexture(_texture);
                GpuMemoryTracker.TrackDeallocation(Width * Height * 4);
            }
            GLHelpers.CheckErrors();
            _texture = 0;
        }

        public void Dispose() {
            ReleaseTexture();
        }
    }
}