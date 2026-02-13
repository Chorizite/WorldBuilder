using Chorizite.Core.Render;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chorizite.OpenGLSDLBackend {
    /// <summary>
    /// Implementation of a framebuffer for OpenGL ES 3.0 using Silk.NET.
    /// </summary>
    public class ManagedGLFramebuffer : IFramebuffer {
        private readonly GL _gl;
        private readonly uint _fboId;
        private readonly uint _depthStencilRenderbuffer; // 0 if not used
        private readonly ITexture _texture;
        private readonly int _width;
        private readonly int _height;

        public ITexture Texture => _texture;
        public IntPtr NativeHandle => new IntPtr(_fboId);

        public ManagedGLFramebuffer(GL gl, ITexture texture, int width, int height, bool hasDepthStencil) {
            _gl = gl;
            _texture = texture;
            _width = width;
            _height = height;

            // Generate and bind the framebuffer
            _fboId = _gl.GenFramebuffer();
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fboId);

            // Attach the texture as the color attachment
            _gl.FramebufferTexture2D(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D,
                (uint)texture.NativePtr.ToInt32(),
                0
            );

            // Create and attach a depth-stencil renderbuffer if requested
            if (true || hasDepthStencil) {
                _depthStencilRenderbuffer = _gl.GenRenderbuffer();
                _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthStencilRenderbuffer);
                _gl.RenderbufferStorage(
                    RenderbufferTarget.Renderbuffer,
                    InternalFormat.Depth24Stencil8,
                    (uint)width,
                    (uint)height
                );
                _gl.FramebufferRenderbuffer(
                    FramebufferTarget.Framebuffer,
                    FramebufferAttachment.DepthStencilAttachment,
                    RenderbufferTarget.Renderbuffer,
                    _depthStencilRenderbuffer
                );
                GpuMemoryTracker.TrackAllocation(_width * _height * 4); // Depth24Stencil8 is 4 bytes per pixel
            }

            // Check framebuffer completeness
            var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != GLEnum.FramebufferComplete) {
                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                _gl.DeleteFramebuffer(_fboId);
                if (_depthStencilRenderbuffer != 0) {
                    _gl.DeleteRenderbuffer(_depthStencilRenderbuffer);
                }
                throw new InvalidOperationException($"Framebuffer creation failed: {status}");
            }

            var error = _gl.GetError();
            if (error != GLEnum.NoError) {
                throw new InvalidOperationException($"OpenGL error during framebuffer setup: {error}");
            }

            // Unbind the framebuffer
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        public void Dispose() {
            _gl.DeleteFramebuffer(_fboId);
            if (_depthStencilRenderbuffer != 0) {
                _gl.DeleteRenderbuffer(_depthStencilRenderbuffer);
                GpuMemoryTracker.TrackDeallocation(_width * _height * 4);
            }
        }
    }
}