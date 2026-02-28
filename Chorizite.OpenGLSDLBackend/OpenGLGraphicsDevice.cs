using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using Chorizite.Core.Render.Vertex;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using PolygonMode = Silk.NET.OpenGL.PolygonMode;
using PrimitiveType = Silk.NET.OpenGL.PrimitiveType;
using WorldBuilder.Shared.Models;

namespace Chorizite.OpenGLSDLBackend {
    /// <summary>
    /// OpenGL graphics device
    /// </summary>
    public unsafe class OpenGLGraphicsDevice : BaseGraphicsDevice {
        private readonly ILogger _log;
        private readonly DebugRenderSettings _renderSettings;

        public GL GL { get; }
        public DebugRenderSettings RenderSettings => _renderSettings;

        public uint InstanceVBO { get; private set; }
        private int _instanceBufferCapacity = 0;
        private int _instanceBufferStride = 0;

        /// <inheritdoc />
        public override IntPtr NativeDevice { get; }

        public OpenGLGraphicsDevice(GL gl, ILogger log, DebugRenderSettings renderSettings) : base() {
            _log = log;
            _renderSettings = renderSettings;

            GL = gl;
            GLHelpers.Init(this, log);

            GL.GenBuffers(1, out uint instanceVbo);
            InstanceVBO = instanceVbo;
        }

        public void EnsureInstanceBufferCapacity(int count, int stride, bool forceOrphan = false) {
            if (count <= _instanceBufferCapacity && !forceOrphan) return;

            if (_instanceBufferCapacity > 0) {
                GpuMemoryTracker.TrackDeallocation(_instanceBufferCapacity * _instanceBufferStride);
            }

            _instanceBufferCapacity = Math.Max(count, 256);
            _instanceBufferStride = stride;
            GL.BindBuffer(GLEnum.ArrayBuffer, InstanceVBO);
            GL.BufferData(GLEnum.ArrayBuffer, (nuint)(_instanceBufferCapacity * _instanceBufferStride),
                (void*)null, GLEnum.DynamicDraw);
            GpuMemoryTracker.TrackAllocation(_instanceBufferCapacity * _instanceBufferStride);
        }

        /// <inheritdoc />
        public override void Clear(ColorVec color, ClearFlags flags, float depth, int stencil) {
            GL.ClearColor(color.R, color.G, color.B, color.A);
            GLHelpers.CheckErrors();
            GL.Clear((uint)Convert(flags));
            GLHelpers.CheckErrors();
        }

        /// <inheritdoc />
        public override IIndexBuffer CreateIndexBuffer(int size,
            Core.Render.Enums.BufferUsage usage = Core.Render.Enums.BufferUsage.Static) {
            return new ManagedGLIndexBuffer(this, usage, size);
        }

        /// <inheritdoc />
        public override IVertexBuffer CreateVertexBuffer(int size,
            Core.Render.Enums.BufferUsage usage = Core.Render.Enums.BufferUsage.Static) {
            return new ManagedGLVertexBuffer(this, usage, size);
        }

        /// <inheritdoc />
        public override IVertexArray CreateArrayBuffer(IVertexBuffer vertexBuffer, VertexFormat format) {
            return new ManagedGLVertexArray(this, vertexBuffer, format);
        }

        /// <inheritdoc />
        public override void DrawElements(Core.Render.Enums.PrimitiveType type, int numElements, int indiceOffset = 0) {
            GL.DrawElements(Convert(type), (uint)numElements, GLEnum.UnsignedInt, (void*)(indiceOffset * sizeof(uint)));
            GLHelpers.CheckErrors();
        }

        /// <inheritdoc />
        public override IShader CreateShader(string name, string vertexCode, string fragmentCode) {
            return new GLSLShader(this, name, vertexCode, fragmentCode, _log);
        }

        /// <inheritdoc />
        public override IShader CreateShader(string name, string shaderDirectory) {
            return new GLSLShader(this, name, shaderDirectory, _log);
        }

        /// <inheritdoc />
        public override ITexture
            CreateTextureInternal(TextureFormat format, int width, int height, byte[]? data = null) {
            if (format != TextureFormat.RGBA8) {
                throw new NotImplementedException($"Texture format {format} is not supported.");
            }

            return new ManagedGLTexture(this, data, width, height);
        }

        /// <inheritdoc />
        public override ITexture? CreateTextureInternal(TextureFormat format, string filename) {
            if (format != TextureFormat.RGBA8) {
                throw new NotImplementedException($"Texture format {format} is not supported.");
            }

            return new ManagedGLTexture(this, filename);
        }

        /// <inheritdoc />
        public override ITextureArray
            CreateTextureArrayInternal(TextureFormat format, int width, int height, int size) {
            return new ManagedGLTextureArray(this, format, width, height, size, _log);
        }

        /// <inheritdoc />
        public override void BeginFrame() {
            GL.Viewport(Viewport.X, Viewport.Y, (uint)Viewport.Width, (uint)Viewport.Height);
            GLHelpers.CheckErrors();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GLHelpers.CheckErrors();
        }

        /// <inheritdoc />
        public override void EndFrame() {
        }

        /// <inheritdoc />
        protected override void SetRenderStateInternal(RenderState state, bool enabled) {
            switch (state) {
                case RenderState.AlphaBlend:
                    if (enabled) GL.Enable(EnableCap.Blend);
                    else GL.Disable(EnableCap.Blend);
                    GLHelpers.CheckErrors();
                    break;
                case RenderState.DepthTest:
                    if (enabled) GL.Enable(EnableCap.DepthTest);
                    else GL.Disable(EnableCap.DepthTest);
                    GLHelpers.CheckErrors();
                    break;
                case RenderState.ScissorTest:
                    if (enabled) GL.Enable(EnableCap.ScissorTest);
                    else GL.Disable(EnableCap.ScissorTest);
                    GLHelpers.CheckErrors();
                    break;
                case RenderState.DepthWrite:
                    if (enabled) GL.DepthMask(true);
                    else GL.DepthMask(false);
                    GLHelpers.CheckErrors();
                    break;
                case RenderState.Fog:
                    break;
                case RenderState.Lighting:
                    break;
            }
        }

        /// <inheritdoc />
        protected override void SetBlendFactorInternal(BlendFactor srcBlendFactor, BlendFactor dstBlendFactor) {
            GL.BlendFunc(Convert(srcBlendFactor), Convert(dstBlendFactor));
            GLHelpers.CheckErrors();
        }

        protected override void SetScissorRectInternal(Rectangle scissor) {
            var gtop = (int)Viewport.Height - scissor.Y - scissor.Height;
            GL.Scissor(scissor.X, gtop, (uint)scissor.Width, (uint)scissor.Height);
            GLHelpers.CheckErrors();
        }

        protected override void SetViewportInternal(Rectangle viewport) {
            GL.Viewport(viewport.X, viewport.Y, (uint)viewport.Width, (uint)viewport.Height);
            GLHelpers.CheckErrors();
        }

        protected override void SetPolygonModeInternal(Core.Render.Enums.PolygonMode polygonMode) {
            GL.PolygonMode(GLEnum.FrontAndBack, Convert(polygonMode));
            GLHelpers.CheckErrors();
        }

        protected override void SetCullModeInternal(CullMode cullMode) {
            switch (cullMode) {
                case CullMode.None:
                    GL.Disable(EnableCap.CullFace);
                    break;
                case CullMode.Front:
                    GL.Enable(EnableCap.CullFace);
                    GL.CullFace(GLEnum.Front);
                    break;
                case CullMode.Back:
                    GL.Enable(EnableCap.CullFace);
                    GL.CullFace(GLEnum.Back);
                    break;
            }
        }

        private GLEnum Convert(Core.Render.Enums.PolygonMode mode) {
            switch (mode) {
                case Core.Render.Enums.PolygonMode.Fill:
                    return GLEnum.Fill;
                case Core.Render.Enums.PolygonMode.Line:
                    return GLEnum.Line;
                case Core.Render.Enums.PolygonMode.Point:
                    return GLEnum.Point;
                default:
                    return GLEnum.Fill;
            }
        }

        private GLEnum Convert(ClearFlags flags) {
            GLEnum mask = 0;

            if ((flags & ClearFlags.Color) == ClearFlags.Color) mask |= GLEnum.ColorBufferBit;
            if ((flags & ClearFlags.Depth) == ClearFlags.Depth) mask |= GLEnum.DepthBufferBit;
            if ((flags & ClearFlags.Stencil) == ClearFlags.Stencil) mask |= GLEnum.StencilBufferBit;

            return mask;
        }

        private GLEnum Convert(BlendFactor factor) {
            switch (factor) {
                case BlendFactor.One:
                    return GLEnum.One;
                case BlendFactor.SrcAlpha:
                    return GLEnum.SrcAlpha;
                case BlendFactor.OneMinusSrcAlpha:
                    return GLEnum.OneMinusSrcAlpha;
                case BlendFactor.DstAlpha:
                    return GLEnum.DstAlpha;
                case BlendFactor.OneMinusDstAlpha:
                    return GLEnum.OneMinusDstAlpha;
                default:
                    return GLEnum.One;
            }
        }

        private PrimitiveType Convert(Core.Render.Enums.PrimitiveType type) {
            switch (type) {
                case Core.Render.Enums.PrimitiveType.PointList:
                    return PrimitiveType.Points;
                case Core.Render.Enums.PrimitiveType.LineList:
                    return PrimitiveType.Lines;
                case Core.Render.Enums.PrimitiveType.LineStrip:
                    return PrimitiveType.LineStrip;
                case Core.Render.Enums.PrimitiveType.TriangleList:
                    return PrimitiveType.Triangles;
                case Core.Render.Enums.PrimitiveType.TriangleStrip:
                    return PrimitiveType.TriangleStrip;
                default:
                    throw new NotImplementedException($"Primitive type {type} is not supported.");
            }
        }

        /// <inheritdoc />
        public override IFramebuffer CreateFramebuffer(ITexture texture, int width, int height,
            bool hasDepthStencil = true) {
            if (texture == null) {
                throw new ArgumentNullException(nameof(texture));
            }

            if (width <= 0 || height <= 0) {
                throw new ArgumentException("Width and height must be positive.");
            }

            return new ManagedGLFramebuffer(GL, texture, width, height, hasDepthStencil);
        }

        /// <inheritdoc />
        public override void BindFramebuffer(IFramebuffer? framebuffer) {
            uint fboId = framebuffer != null ? (uint)framebuffer.NativeHandle.ToInt32() : 0;
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboId);
        }

        /// <inheritdoc />
        public override void Dispose() {
            if (InstanceVBO != 0) {
                GL.DeleteBuffer(InstanceVBO);
                if (_instanceBufferCapacity > 0) {
                    GpuMemoryTracker.TrackDeallocation(_instanceBufferCapacity * _instanceBufferStride);
                }
                InstanceVBO = 0;
            }
        }

        public override IUniformBuffer CreateUniformBuffer(BufferUsage usage, int size) {
            throw new NotImplementedException();
        }
    }
}