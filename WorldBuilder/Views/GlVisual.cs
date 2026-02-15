using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using System;
using System.Runtime.InteropServices;

namespace WorldBuilder.Views {
    public abstract partial class Base3DViewport {
private class GlVisual : CompositionCustomVisualHandler {
            private bool _contentInitialized;
            private IGlContext? _gl;
            private PixelSize _lastSize;
            private readonly Base3DViewport _parent;
            private PixelPoint _position;
            private double _scaling = 1.0;
            private int _surfaceHeight;
            private PixelSize _controlSize;

            public DateTime LastRenderTime { get; private set; } = DateTime.MinValue;
            public GL? SilkGl { get; private set; }

            public GlVisual(Base3DViewport parent) {
                _parent = parent;
            }

            public override void OnRender(ImmediateDrawingContext drawingContext) {
                try {
                    var frameTime = CalculateFrameTime();
                    RegisterForNextAnimationFrameUpdate();

                    if (!drawingContext.TryGetFeature<ISkiaSharpApiLeaseFeature>(out var skiaFeature)) {
                        throw new Exception("Unable to get ISkiaSharpApiLeaseFeature");
                    }

                    using var skiaLease = skiaFeature.Lease();
                    _ = skiaLease.GrContext ?? throw new Exception("Unable to get GrContext");

                    // Use stored control size and scaling
                    var controlSize = _controlSize;

                    if (controlSize.Width < 1 || controlSize.Height < 1) {
                        return;
                    }

                    using (var platformApiLease = skiaLease.TryLeasePlatformGraphicsApi()) {
                        if (platformApiLease?.Context is not IGlContext glContext) {
                            throw new Exception("Unable to get IGlContext");
                        }

                        InitializeContextIfNeeded(glContext, controlSize);

                        if (SilkGl == null) {
                            throw new Exception("SilkGl not initialized");
                        }

                        // Save current framebuffer
                        SilkGl.GetInteger(GetPName.DrawFramebufferBinding, out int oldFramebufferBinding);

                        // Handle resize
                        if (_lastSize != controlSize) {
                            _parent.OnGlResizeInternal(controlSize);
                            _lastSize = controlSize;

                            if (_gl != null && _parent is RenderView) {
                                RenderView.SharedContextManager.SetViewportDimensions(_gl, controlSize.Width, controlSize.Height);
                            }
                        }

                        // Save and set OpenGL state for rendering
                        var state = SaveGlState(SilkGl);
                        SetupGlStateForRendering(SilkGl);

                        // Render to FBO (with scissor disabled to avoid clipping our internal render)
                        SilkGl.Disable(EnableCap.ScissorTest);
                        _parent.OnGlRenderInternal(frameTime);

                        // Restore scissor before blitting to prevent drawing outside our bounds
                        RestoreScissorState(SilkGl, state);

                        // Blit FBO to screen
                        BlitFramebuffer(SilkGl, oldFramebufferBinding, controlSize);

                        // Restore full OpenGL state
                        RestoreGlState(SilkGl, state);

                        // Restore framebuffer
                        SilkGl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)oldFramebufferBinding);
                    }
                }
                catch (Exception ex) {
                    _parent._logger.LogError(ex, "Render error");
                }
            }

            private void InitializeContextIfNeeded(IGlContext glContext, PixelSize initialSize) {
                if (_gl == glContext && _contentInitialized) {
                    return;
                }

                _contentInitialized = false;
                _gl = glContext;
                SilkGl = GL.GetApi(glContext.GlInterface.GetProcAddress);

                // Register master context for RenderView
                if (_parent is RenderView) {
                    var sharedContextManager = RenderView.SharedContextManager;
                    if (sharedContextManager.GetMasterContext().context == null) {
                        sharedContextManager.SetMasterContext(glContext, SilkGl);
                    }
                }

                _parent._logger.LogDebug("New OpenGL context assigned: {ContextHashCode}", glContext.GetHashCode());

                // Initialize with a valid size
                var initSize = initialSize.Width > 0 && initialSize.Height > 0 ? initialSize : new PixelSize(1, 1);
                _parent.OnGlInitInternal(SilkGl, initSize);
                _contentInitialized = true;

                var version = SilkGl.GetStringS(StringName.Version);
                var vendor = SilkGl.GetStringS(StringName.Vendor);
                var renderer = SilkGl.GetStringS(StringName.Renderer);
                _parent._logger.LogInformation("OpenGL Version: {Version} // {Vendor} // {Renderer}", version, vendor, renderer);
            }

            private GlState SaveGlState(GL gl) {
                var state = new GlState();
                gl.GetInteger(GetPName.Viewport, state.Viewport);
                state.DepthTest = gl.IsEnabled(EnableCap.DepthTest);
                state.CullFace = gl.IsEnabled(EnableCap.CullFace);
                gl.GetInteger(GetPName.CullFaceMode, out state.CullFaceMode);
                gl.GetInteger(GetPName.FrontFace, out state.FrontFace);
                state.Blend = gl.IsEnabled(EnableCap.Blend);
                gl.GetInteger(GetPName.BlendSrcRgb, out state.BlendSrc);
                gl.GetInteger(GetPName.BlendDstRgb, out state.BlendDst);
                gl.GetInteger(GetPName.BlendEquationRgb, out state.BlendEquation);
                state.Scissor = gl.IsEnabled(EnableCap.ScissorTest);
                gl.GetInteger(GetPName.ScissorBox, state.ScissorBox);
                return state;
            }

            private void SetupGlStateForRendering(GL gl) {
                gl.Enable(EnableCap.DepthTest);
                gl.DepthFunc(DepthFunction.Less);
                gl.Enable(EnableCap.CullFace);
                gl.CullFace(TriangleFace.Back);
                gl.FrontFace(FrontFaceDirection.CW);
                gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
            }

            private void RestoreScissorState(GL gl, GlState state) {
                if (state.Scissor) {
                    gl.Enable(EnableCap.ScissorTest);
                    gl.Scissor(state.ScissorBox[0], state.ScissorBox[1], (uint)state.ScissorBox[2], (uint)state.ScissorBox[3]);
                }
            }

            private void RestoreGlState(GL gl, GlState state) {
                gl.Viewport(state.Viewport[0], state.Viewport[1], (uint)state.Viewport[2], (uint)state.Viewport[3]);
                
                if (state.Scissor) gl.Enable(EnableCap.ScissorTest); else gl.Disable(EnableCap.ScissorTest);
                gl.Scissor(state.ScissorBox[0], state.ScissorBox[1], (uint)state.ScissorBox[2], (uint)state.ScissorBox[3]);
                
                if (state.DepthTest) gl.Enable(EnableCap.DepthTest); else gl.Disable(EnableCap.DepthTest);
                if (state.CullFace) gl.Enable(EnableCap.CullFace); else gl.Disable(EnableCap.CullFace);
                gl.CullFace((TriangleFace)state.CullFaceMode);
                gl.FrontFace((FrontFaceDirection)state.FrontFace);
                
                if (state.Blend) gl.Enable(EnableCap.Blend); else gl.Disable(EnableCap.Blend);
                gl.BlendFunc((BlendingFactor)state.BlendSrc, (BlendingFactor)state.BlendDst);
                gl.BlendEquation((BlendEquationModeEXT)state.BlendEquation);
            }

            private void BlitFramebuffer(GL gl, int targetFramebuffer, PixelSize controlSize) {
                if (_parent.RenderTarget?.Framebuffer == null || _parent.RenderTarget?.Texture == null) {
                    return;
                }

                var textureFB = (uint)_parent.RenderTarget.Framebuffer.NativeHandle;
                gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, textureFB);
                gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, (uint)targetFramebuffer);

                var srcWidth = _parent.RenderTarget.Texture.Width;
                var srcHeight = _parent.RenderTarget.Texture.Height;

                var destX = _position.X;
                var destY = 0;
                var destW = controlSize.Width;
                var destH = controlSize.Height;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                    destY = _position.Y;
                    gl.BlitFramebuffer(
                        0, 0, srcWidth, srcHeight,
                        destX, destY + destH, destX + destW, destY,
                        ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Linear
                    );
                }
                else {
                    destY = _surfaceHeight - (_position.Y + controlSize.Height);
                    gl.BlitFramebuffer(
                        0, 0, srcWidth, srcHeight,
                        destX, destY, destX + destW, destY + destH,
                        ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Linear
                    );
                }
            }

            private double CalculateFrameTime() {
                var now = DateTime.Now;
                var frameTime = LastRenderTime == DateTime.MinValue ? 0 : (now - LastRenderTime).TotalSeconds;
                LastRenderTime = now;
                return frameTime;
            }

            public override void OnAnimationFrameUpdate() {
                Invalidate();
                base.OnAnimationFrameUpdate();
            }

            public override void OnMessage(object message) {
                if (message is DisposeMessage) {
                    DisposeResources();
                }
                else if (message is PositionMessage pos) {
                    _position = pos.Position;
                    _scaling = pos.Scaling;
                    _surfaceHeight = pos.SurfaceHeight;
                    _controlSize = pos.Size;
                }
                base.OnMessage(message);
            }

            private void DisposeResources() {
                if (_gl == null) return;

                try {
                    if (_contentInitialized) {
                        using (_gl.MakeCurrent()) {
                            _parent.OnGlDestroyInternal();
                            _contentInitialized = false;
                        }
                    }
                }
                catch (Exception ex) {
                    _parent._logger.LogError(ex, "Error disposing resources");
                }
                finally {
                    _gl = null;
                }
            }

            private struct GlState {
                public int[] Viewport;
                public bool DepthTest;
                public bool CullFace;
                public int CullFaceMode;
                public int FrontFace;
                public bool Blend;
                public int BlendSrc;
                public int BlendDst;
                public int BlendEquation;
                public bool Scissor;
                public int[] ScissorBox;

                public GlState() {
                    Viewport = new int[4];
                    ScissorBox = new int[4];
                }
            }
        }
    }
}