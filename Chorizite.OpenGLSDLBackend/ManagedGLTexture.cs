using Chorizite.Core.Dats;
using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using Chorizite.OpenGLSDLBackend.Lib;
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
        public ulong BindlessHandle { get; private set; }

        /// <inheritdoc/>
        public ManagedGLTexture(OpenGLGraphicsDevice device, byte[]? source, int width, int height, TextureParameters? texParams = null) {
            var p = texParams ?? TextureParameters.Default;
            _device = device;
            _texture = GL.GenTexture();
            GpuMemoryTracker.TrackResourceAllocation(GpuResourceType.Texture);
            Width = width;
            Height = height;
            GL.BindTexture(GLEnum.Texture2D, _texture);
            GLHelpers.CheckErrors(GL);

            int maxDimension = Math.Max(width, height);
            int mipLevels = (int)Math.Floor(Math.Log2(maxDimension)) + 1;

            if (_device.HasTextureStorage) {
                GL.TexStorage2D(GLEnum.Texture2D, (uint)mipLevels, GLEnum.Rgba8, (uint)width, (uint)height);
                GLHelpers.CheckErrors(GL);
            }
            else {
                GL.TexImage2D(GLEnum.Texture2D, 0, (int)InternalFormat.Rgba8, (uint)width, (uint)height, 0, PixelFormat.Rgba, (PixelType)0x1401, (void*)0);
                GLHelpers.CheckErrors(GL);
            }

            GL.TexParameter(GLEnum.Texture2D, TextureParameterName.TextureWrapS, (int)p.WrapS);
            GL.TexParameter(GLEnum.Texture2D, TextureParameterName.TextureWrapT, (int)p.WrapT);
            GL.TexParameter(GLEnum.Texture2D, TextureParameterName.TextureMinFilter, (int)p.MinFilter);
            GL.TexParameter(GLEnum.Texture2D, TextureParameterName.TextureMagFilter, (int)p.MagFilter);
            GLHelpers.CheckErrors(GL);

            if (p.EnableAnisotropicFiltering && _device.RenderSettings.EnableAnisotropicFiltering) 
            {
                float maxAnisotropy = 0f;
                GL.GetFloat(GLEnum.MaxTextureMaxAnisotropy, out maxAnisotropy);

                if (maxAnisotropy > 0) 
                {
                    GL.TexParameter(GLEnum.Texture2D, GLEnum.TextureMaxAnisotropy, maxAnisotropy);
                }
            }

            if (p.EnableMipmaps) {
                GL.GenerateMipmap(GLEnum.Texture2D);
            }
            GLHelpers.CheckErrors(GL);
            GL.BindTexture(GLEnum.Texture2D, 0);
            GLHelpers.CheckErrors(GL);

            GpuMemoryTracker.TrackAllocation(CalculateSize(), GpuResourceType.Texture);

            if (_device.HasBindless && _device.BindlessExtension != null) {
                BindlessHandle = _device.BindlessExtension.GetTextureHandle(_texture);
                _device.BindlessExtension.MakeTextureHandleResident(BindlessHandle);
            }
        }

        private long CalculateSize() {
            int maxDimension = Math.Max(Width, Height);
            int mipLevels = (int)Math.Floor(Math.Log2(maxDimension)) + 1;
            long totalSize = 0;

            for (int i = 0; i < mipLevels; i++) {
                int w = Math.Max(1, Width >> i);
                int h = Math.Max(1, Height >> i);
                totalSize += (long)w * h * 4;
            }
            return totalSize;
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
            
            GLHelpers.CheckErrors(GL);

            GL.GetInteger(GLEnum.ActiveTexture, out int oldActiveTexture);
            BaseObjectRenderManager.CurrentAtlas = 0;
            
            GL.GetInteger(GLEnum.TextureBinding2D, out int oldBinding);
            GL.BindTexture(GLEnum.Texture2D, _texture);

            bool wasResident = false;
            if (BindlessHandle != 0 && _device.BindlessExtension != null && _device.BindlessExtension.IsTextureHandleResident(BindlessHandle)) {
                _device.BindlessExtension.MakeTextureHandleNonResident(BindlessHandle);
                wasResident = true;
            }

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
            }

            // Generate mipmaps if needed
            GL.GenerateMipmap(GLEnum.Texture2D);

            if (wasResident && BindlessHandle != 0 && _device.BindlessExtension != null) {
                _device.BindlessExtension.MakeTextureHandleResident(BindlessHandle);
            }

            GL.BindTexture(GLEnum.Texture2D, (uint)oldBinding);
            GL.ActiveTexture((GLEnum)oldActiveTexture);
            GLHelpers.CheckErrors(GL);
        }

        public void Bind(int slot = 0) {
            if (slot == 0) {
                BaseObjectRenderManager.CurrentAtlas = 0;
            }
            GL.GetInteger(GLEnum.ActiveTexture, out int oldActiveTexture);
            GLEnum targetTextureUnit = GLEnum.Texture0 + slot;
            bool changedUnit = (GLEnum)oldActiveTexture != targetTextureUnit;

            if (changedUnit) {
                GL.ActiveTexture(targetTextureUnit);
            }
            
            GL.BindSampler((uint)slot, 0);
            GL.BindTexture(GLEnum.Texture2D, (uint)NativePtr);
            
            if (changedUnit) {
                GL.ActiveTexture((GLEnum)oldActiveTexture);
            }
            GLHelpers.CheckErrors(GL);
        }

        public void Unbind() {
            GL.BindTexture(GLEnum.Texture2D, 0);
            GLHelpers.CheckErrors(GL);
        }

        protected void ReleaseTexture() {
            if (BindlessHandle != 0 && _device.BindlessExtension != null) {
                _device.BindlessExtension.MakeTextureHandleNonResident(BindlessHandle);
                BindlessHandle = 0;
            }
            if (_texture != 0) {
                GL.DeleteTexture(_texture);
                GpuMemoryTracker.TrackResourceDeallocation(GpuResourceType.Texture);
                GpuMemoryTracker.TrackDeallocation(CalculateSize(), GpuResourceType.Texture);
            }
            GLHelpers.CheckErrors(GL);
            _texture = 0;
        }

        public void Dispose() {
            ReleaseTexture();
        }
    }
}