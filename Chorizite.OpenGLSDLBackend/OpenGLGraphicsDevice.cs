using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using Chorizite.Core.Render.Vertex;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ARB;
using PolygonMode = Silk.NET.OpenGL.PolygonMode;
using PrimitiveType = Silk.NET.OpenGL.PrimitiveType;
using WorldBuilder.Shared.Models;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Chorizite.OpenGLSDLBackend {
    /// <summary>
    /// OpenGL graphics device
    /// </summary>
    public unsafe class OpenGLGraphicsDevice : BaseGraphicsDevice {
        private readonly ILogger _log;
        private readonly DebugRenderSettings _renderSettings;

        public GL GL { get; }
        public DebugRenderSettings RenderSettings => _renderSettings;

        private readonly ConcurrentQueue<Action<GL>> _glThreadQueue = new();

        public void QueueGLAction(Action<GL> action) {
            _glThreadQueue.Enqueue(action);
        }

        public void ProcessGLQueue() {
            while (_glThreadQueue.TryDequeue(out var action)) {
                try {
                    action(GL);
                } catch (Exception ex) {
                    _log.LogError(ex, "Error processing GL queue action");
                }
            }
        }

        public bool HasBindless { get; private set; }
        public bool HasOpenGL43 { get; private set; }
        public bool HasBufferStorage { get; private set; }
        public bool HasTextureStorage { get; private set; }
        public ArbBindlessTexture? BindlessExtension { get; private set; }

        public uint InstanceVBO { get; private set; }
        public void* InstanceVBOPtr { get; private set; }

        /// <summary>OpenGL sampler object with TextureWrapMode.Repeat (for meshes with wrapping UVs).</summary>
        public uint WrapSampler { get; private set; }
        /// <summary>OpenGL sampler object with TextureWrapMode.ClampToEdge (for meshes without wrapping UVs).</summary>
        public uint ClampSampler { get; private set; }

        private int _instanceBufferCapacity = 0;
        private int _instanceBufferStride = 0;

        /// <inheritdoc />
        public override IntPtr NativeDevice { get; }

        protected OpenGLGraphicsDevice() : base() {
            _log = null!;
            _renderSettings = null!;
            GL = null!;
        }

        public OpenGLGraphicsDevice(GL gl, ILogger log, DebugRenderSettings renderSettings, bool allowBindless = true) : base() {
            _log = log;
            _renderSettings = renderSettings;

            GL = gl;
            GLHelpers.Init(this, log);

            try {
                GL.GetInteger(GLEnum.MajorVersion, out int major);
                GL.GetInteger(GLEnum.MinorVersion, out int minor);
                HasOpenGL43 = major > 4 || (major == 4 && minor >= 3);
                HasTextureStorage = major > 4 || (major == 4 && minor >= 2) || GL.IsExtensionPresent("GL_ARB_texture_storage");
                HasBufferStorage = major > 4 || (major == 4 && minor >= 4) || GL.IsExtensionPresent("GL_ARB_buffer_storage");
                
                if (allowBindless && GL.TryGetExtension(out ArbBindlessTexture ext)) {
                    BindlessExtension = ext;
                    HasBindless = true;
                } else {
                    HasBindless = false;
                }
            } catch {
                HasOpenGL43 = false;
                HasBindless = false;
            }

            GL.GenBuffers(1, out uint instanceVbo);
            InstanceVBO = instanceVbo;

            // Create sampler objects for wrap vs clamp
            WrapSampler = GL.GenSampler();
            GL.SamplerParameter(WrapSampler, SamplerParameterI.WrapS, (int)TextureWrapMode.Repeat);
            GL.SamplerParameter(WrapSampler, SamplerParameterI.WrapT, (int)TextureWrapMode.Repeat);
            GL.SamplerParameter(WrapSampler, SamplerParameterI.MinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.SamplerParameter(WrapSampler, SamplerParameterI.MagFilter, (int)TextureMagFilter.Linear);
            if (renderSettings.EnableAnisotropicFiltering) {
                GL.GetFloat(GLEnum.MaxTextureMaxAnisotropy, out float maxAniso);
                if (maxAniso > 0) GL.SamplerParameter(WrapSampler, GLEnum.TextureMaxAnisotropy, maxAniso);
            }

            ClampSampler = GL.GenSampler();
            GL.SamplerParameter(ClampSampler, SamplerParameterI.WrapS, (int)TextureWrapMode.ClampToEdge);
            GL.SamplerParameter(ClampSampler, SamplerParameterI.WrapT, (int)TextureWrapMode.ClampToEdge);
            GL.SamplerParameter(ClampSampler, SamplerParameterI.MinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.SamplerParameter(ClampSampler, SamplerParameterI.MagFilter, (int)TextureMagFilter.Linear);
            if (renderSettings.EnableAnisotropicFiltering) {
                GL.GetFloat(GLEnum.MaxTextureMaxAnisotropy, out float maxAniso);
                if (maxAniso > 0) GL.SamplerParameter(ClampSampler, GLEnum.TextureMaxAnisotropy, maxAniso);
            }
        }

        public void EnsureInstanceBufferCapacity(int count, int stride, bool forceOrphan = false) {
            if (count <= _instanceBufferCapacity && !forceOrphan) return;

            if (_instanceBufferCapacity > 0) {
                GpuMemoryTracker.TrackDeallocation(_instanceBufferCapacity * _instanceBufferStride);
            }

            _instanceBufferCapacity = Math.Max(count, 256);
            _instanceBufferStride = stride;

            if (HasBufferStorage) {
                if (InstanceVBO != 0) {
                    GL.DeleteBuffer(InstanceVBO);
                }
                GL.GenBuffers(1, out uint instanceVbo);
                InstanceVBO = instanceVbo;
                GL.BindBuffer(GLEnum.ArrayBuffer, InstanceVBO);
                var flags = BufferStorageMask.MapWriteBit | BufferStorageMask.MapPersistentBit | BufferStorageMask.MapCoherentBit | BufferStorageMask.DynamicStorageBit;
                GL.BufferStorage(GLEnum.ArrayBuffer, (nuint)(_instanceBufferCapacity * _instanceBufferStride), (void*)0, flags);
                InstanceVBOPtr = GL.MapBufferRange(GLEnum.ArrayBuffer, 0, (nuint)(_instanceBufferCapacity * _instanceBufferStride), MapBufferAccessMask.WriteBit | MapBufferAccessMask.PersistentBit | MapBufferAccessMask.CoherentBit);
            } else {
                GL.BindBuffer(GLEnum.ArrayBuffer, InstanceVBO);
                GL.BufferData(GLEnum.ArrayBuffer, (nuint)(_instanceBufferCapacity * _instanceBufferStride),
                    (void*)null, GLEnum.DynamicDraw);
                InstanceVBOPtr = null;
            }
            GpuMemoryTracker.TrackAllocation(_instanceBufferCapacity * _instanceBufferStride);
        }

        public void UpdateInstanceBuffer<T>(List<T> data) where T : unmanaged {
            EnsureInstanceBufferCapacity(data.Count, Marshal.SizeOf<T>(), true);
            var span = CollectionsMarshal.AsSpan(data);
            if (InstanceVBOPtr != null) {
                var destSpan = new Span<T>(InstanceVBOPtr, data.Count);
                span.CopyTo(destSpan);
            } else {
                GL.BindBuffer(GLEnum.ArrayBuffer, InstanceVBO);
                fixed (T* ptr = span) {
                    GL.BufferSubData(GLEnum.ArrayBuffer, 0, (nuint)(data.Count * Marshal.SizeOf<T>()), ptr);
                }
            }
        }

        public void UpdateInstanceBuffer<T>(Span<T> data) where T : unmanaged {
            EnsureInstanceBufferCapacity(data.Length, Marshal.SizeOf<T>(), true);
            if (InstanceVBOPtr != null) {
                var destSpan = new Span<T>(InstanceVBOPtr, data.Length);
                data.CopyTo(destSpan);
            } else {
                GL.BindBuffer(GLEnum.ArrayBuffer, InstanceVBO);
                fixed (T* ptr = data) {
                    GL.BufferSubData(GLEnum.ArrayBuffer, 0, (nuint)(data.Length * Marshal.SizeOf<T>()), ptr);
                }
            }
        }

        /// <inheritdoc />
        public override void Clear(ColorVec color, ClearFlags flags, float depth, int stencil) {
            GL.ClearColor(color.R, color.G, color.B, color.A);
            GLHelpers.CheckErrors(GL);
            GL.Clear((uint)Convert(flags));
            GLHelpers.CheckErrors(GL);
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
            GLHelpers.CheckErrors(GL);
        }

        public override IShader CreateShader(string name, string vertexCode, string fragmentCode) {
            var key = $"{GL.GetHashCode()}_{name}_{vertexCode.GetHashCode()}_{fragmentCode.GetHashCode()}";
            
            while (true) {
                if (_shaderCache.TryGetValue(key, out var existing)) {
                    if (existing is SharedShader shared && shared.TryIncrement()) {
                        return existing;
                    }
                }

                var inner = new GLSLShader(this, name, vertexCode, fragmentCode, _log);
                var newShader = new SharedShader(inner, () => _shaderCache.TryRemove(key, out _));

                if (_shaderCache.TryAdd(key, newShader)) {
                    return newShader;
                }
                
                // Someone else added it first, dispose ours and try again
                newShader.DisposeInternal();
            }
        }

        /// <inheritdoc />
        public override IShader CreateShader(string name, string shaderDirectory) {
            var key = $"{GL.GetHashCode()}_{name}";
            
            while (true) {
                if (_shaderCache.TryGetValue(key, out var existing)) {
                    if (existing is SharedShader shared && shared.TryIncrement()) {
                        return existing;
                    }
                }

                var inner = new GLSLShader(this, name, shaderDirectory, _log);
                var newShader = new SharedShader(inner, () => _shaderCache.TryRemove(key, out _));

                if (_shaderCache.TryAdd(key, newShader)) {
                    return newShader;
                }
                
                // Someone else added it first, dispose ours and try again
                newShader.DisposeInternal();
            }
        }

        private static readonly ConcurrentDictionary<string, IShader> _shaderCache = new();

        private class SharedShader : IShader, IDisposable {
            private readonly IShader _shader;
            private readonly Action _onDispose;
            private int _refCount = 1;

            public string Name => _shader.Name;
            public uint ProgramId => _shader.ProgramId;

            public SharedShader(IShader shader, Action onDispose) {
                _shader = shader;
                _onDispose = onDispose;
            }

            public bool TryIncrement() {
                while (true) {
                    int current = _refCount;
                    if (current <= 0) return false;
                    if (Interlocked.CompareExchange(ref _refCount, current + 1, current) == current) {
                        return true;
                    }
                }
            }

            public void Bind() => _shader.Bind();
            public void Unbind() => _shader.Unbind();
            public void Load(string vertexSource, string fragmentSource) => _shader.Load(vertexSource, fragmentSource);

            public void SetUniform(string name, int value) => _shader.SetUniform(name, value);
            public void SetUniform(string name, float value) => _shader.SetUniform(name, value);
            public void SetUniform(string name, Vector2 value) => _shader.SetUniform(name, value);
            public void SetUniform(string name, Vector3 value) => _shader.SetUniform(name, value);
            public void SetUniform(string name, Vector4 value) => _shader.SetUniform(name, value);
            public void SetUniform(string name, Matrix4x4 value) => _shader.SetUniform(name, value);
            public void SetUniform(string name, float[] values) => _shader.SetUniform(name, values);

            public void DisposeInternal() {
                _refCount = 0;
                (_shader as IDisposable)?.Dispose();
            }

            public void Dispose() {
                if (Interlocked.Decrement(ref _refCount) == 0) {
                    (_shader as IDisposable)?.Dispose();
                    _onDispose();
                }
            }
        }

        /// <inheritdoc />
        public override ITexture
            CreateTextureInternal(TextureFormat format, int width, int height, byte[]? data = null) {
            if (format != TextureFormat.RGBA8) {
                throw new NotImplementedException($"Texture format {format} is not supported.");
            }

            return new ManagedGLTexture(this, data, width, height);
        }

        /// <summary>
        /// Creates a texture with custom texture parameters.
        /// </summary>
        public ITexture CreateTextureInternal(TextureFormat format, int width, int height, byte[]? data, TextureParameters texParams) {
            if (format != TextureFormat.RGBA8) {
                throw new NotImplementedException($"Texture format {format} is not supported.");
            }
            return new ManagedGLTexture(this, data, width, height, texParams);
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

        /// <summary>
        /// Creates a texture array with custom texture parameters.
        /// </summary>
        public ITextureArray CreateTextureArrayInternal(TextureFormat format, int width, int height, int size, TextureParameters texParams) {
            return new ManagedGLTextureArray(this, format, width, height, size, _log, texParams);
        }

        /// <inheritdoc />
        public override void BeginFrame() {
            GL.Viewport(Viewport.X, Viewport.Y, (uint)Viewport.Width, (uint)Viewport.Height);
            GLHelpers.CheckErrors(GL);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GLHelpers.CheckErrors(GL);
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
                    GLHelpers.CheckErrors(GL);
                    break;
                case RenderState.DepthTest:
                    if (enabled) GL.Enable(EnableCap.DepthTest);
                    else GL.Disable(EnableCap.DepthTest);
                    GLHelpers.CheckErrors(GL);
                    break;
                case RenderState.ScissorTest:
                    if (enabled) GL.Enable(EnableCap.ScissorTest);
                    else GL.Disable(EnableCap.ScissorTest);
                    GLHelpers.CheckErrors(GL);
                    break;
                case RenderState.DepthWrite:
                    if (enabled) GL.DepthMask(true);
                    else GL.DepthMask(false);
                    GLHelpers.CheckErrors(GL);
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
            GLHelpers.CheckErrors(GL);
        }

        protected override void SetScissorRectInternal(Rectangle scissor) {
            var gtop = (int)Viewport.Height - scissor.Y - scissor.Height;
            GL.Scissor(scissor.X, gtop, (uint)scissor.Width, (uint)scissor.Height);
            GLHelpers.CheckErrors(GL);
        }

        protected override void SetViewportInternal(Rectangle viewport) {
            GL.Viewport(viewport.X, viewport.Y, (uint)viewport.Width, (uint)viewport.Height);
            GLHelpers.CheckErrors(GL);
        }

        protected override void SetPolygonModeInternal(Core.Render.Enums.PolygonMode polygonMode) {
            GL.PolygonMode(GLEnum.FrontAndBack, Convert(polygonMode));
            GLHelpers.CheckErrors(GL);
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

            return new ManagedGLFramebuffer(this, texture, width, height, hasDepthStencil);
        }

        /// <inheritdoc />
        public override void BindFramebuffer(IFramebuffer? framebuffer) {
            uint fboId = framebuffer != null ? (uint)framebuffer.NativeHandle.ToInt32() : 0;
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboId);
        }

        /// <inheritdoc />
        public override void Dispose() {
            var instanceVBO = InstanceVBO;
            var instanceBufferCapacity = _instanceBufferCapacity;
            var instanceBufferStride = _instanceBufferStride;
            var wrapSampler = WrapSampler;
            var clampSampler = ClampSampler;

            QueueGLAction(gl => {
                if (instanceVBO != 0) {
                    gl.DeleteBuffer(instanceVBO);
                    if (instanceBufferCapacity > 0) {
                        GpuMemoryTracker.TrackDeallocation(instanceBufferCapacity * instanceBufferStride);
                    }
                }
                if (wrapSampler != 0) {
                    gl.DeleteSampler(wrapSampler);
                }
                if (clampSampler != 0) {
                    gl.DeleteSampler(clampSampler);
                }
            });

            InstanceVBO = 0;
            InstanceVBOPtr = null;
            WrapSampler = 0;
            ClampSampler = 0;
        }

        public override IUniformBuffer CreateUniformBuffer(BufferUsage usage, int size) {
            return (IUniformBuffer)new ManagedGLUniformBuffer(this, usage, size);
        }
    }
}