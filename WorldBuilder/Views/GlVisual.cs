using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using WorldBuilder.Services;
using Silk.NET.OpenGL;
using System;
using System.Runtime.InteropServices;
using Chorizite.OpenGLSDLBackend.Lib;

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
            private PixelRect _clip;

            public DateTime LastRenderTime { get; private set; } = DateTime.MinValue;
            public GL? SilkGl { get; private set; }

            public GlVisual(Base3DViewport parent) {
                _parent = parent;
            }

            private void TriggerInvalidate() => base.Invalidate();

            public override void OnRender(ImmediateDrawingContext drawingContext) {
                try {
                    var frameTime = CalculateFrameTime();

                    if (_parent.RenderContinuously || _parent._renderRequested) {
                        RegisterForNextAnimationFrameUpdate();
                    }
                    _parent._renderRequested = false;

                    var perfService = WorldBuilder.App.Services?.GetService<PerformanceService>();
                    if (perfService != null) {
                        perfService.FrameTime = frameTime * 1000.0;
                    }

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
                        using (var state = new GLStateScope(SilkGl)) {
                            SetupGlStateForRendering(SilkGl);

                            // Render to FBO (with scissor disabled to avoid clipping our internal render)
                            SilkGl.Disable(EnableCap.ScissorTest);

                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            _parent.OnGlRenderInternal(frameTime);
                            sw.Stop();

                            if (perfService != null) {
                                perfService.RenderTime = sw.Elapsed.TotalMilliseconds;
                            }

                            if (_parent._renderRequested) {
                                RegisterForNextAnimationFrameUpdate();
                            }

                            // Restore scissor before blitting to prevent drawing outside our bounds
                            state.RestoreScissor();

                            // Blit FBO to screen
                            BlitFramebuffer(SilkGl, oldFramebufferBinding, controlSize);
                        }
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

                // Register master context
                var sharedContextManager = RenderView.SharedContextManager;
                if (sharedContextManager.GetMasterContext().context == null) {
                    sharedContextManager.SetMasterContext(glContext, SilkGl);
                }

                // Initialize with a valid size
                var initSize = initialSize.Width > 0 && initialSize.Height > 0 ? initialSize : new PixelSize(1, 1);
                _parent.OnGlInitInternal(SilkGl, initSize);
                _contentInitialized = true;

                var version = SilkGl.GetStringS(StringName.Version);
                var vendor = SilkGl.GetStringS(StringName.Vendor);
                var renderer = SilkGl.GetStringS(StringName.Renderer);
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

                // Apply manual scissor if we have a clip
                gl.Enable(EnableCap.ScissorTest);
                int scissorX = _position.X + _clip.X;
                int scissorY = _surfaceHeight - (_position.Y + _clip.Y + _clip.Height);
                gl.Scissor(scissorX, scissorY, (uint)Math.Max(0, _clip.Width), (uint)Math.Max(0, _clip.Height));

                destY = _surfaceHeight - (_position.Y + controlSize.Height);
                gl.BlitFramebuffer(
                    0, 0, srcWidth, srcHeight,
                    destX, destY, destX + destW, destY + destH,
                    ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Linear
                );
            }

            private double CalculateFrameTime() {
                var now = DateTime.Now;
                var frameTime = LastRenderTime == DateTime.MinValue ? 0 : (now - LastRenderTime).TotalSeconds;
                LastRenderTime = now;
                return frameTime;
            }

            public override void OnAnimationFrameUpdate() {
                if (_parent.RenderContinuously || _parent._renderRequested) {
                    TriggerInvalidate();
                }
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
                    _clip = pos.Clip;
                }
                else if (message is Base3DViewport.InvalidateMessage) {
                    TriggerInvalidate();
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

        }
    }
}