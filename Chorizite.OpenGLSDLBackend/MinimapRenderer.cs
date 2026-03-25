using Silk.NET.OpenGL;
using System.Numerics;
using Chorizite.Core.Render;

namespace Chorizite.OpenGLSDLBackend.Lib;

public class MinimapRenderer : IDisposable {
    private readonly GL _gl;
    private uint _fbo;
    private uint _texture;
    private uint _depthBuffer;
    private const int MapSize = 1024;

    public uint MinimapTexture => _texture;

    public MinimapRenderer(GL gl) {
        _gl = gl;
        Initialize();
    }

    private unsafe void Initialize() {
        _texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _texture);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, (int)InternalFormat.Rgba, MapSize, MapSize, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);

        _depthBuffer = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthBuffer);
        _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, MapSize, MapSize);

        _fbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _texture, 0);
        _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _depthBuffer);

        if (_gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete) {
            throw new Exception("Framebuffer for Minimap is not complete!");
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void RenderToMap(TerrainRenderManager terrainManager, Vector3 centerPos, float range) {
        var projection = Matrix4x4.CreateOrthographic(range, range, 1f, 10000f);
        var view = Matrix4x4.CreateLookAt(centerPos + new Vector3(0, 0, 5000), centerPos, new Vector3(0, 1, 0));

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.Viewport(0, 0, MapSize, MapSize);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        terrainManager.Render(view, projection, view * projection, centerPos, 90f);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void Dispose() {
        _gl.DeleteFramebuffer(_fbo);
        _gl.DeleteTexture(_texture);
        _gl.DeleteRenderbuffer(_depthBuffer);
    }
}