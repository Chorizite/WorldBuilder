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

namespace WorldBuilder.Views {
    public abstract partial class Base3DViewport {
        private class GlVisual : CompositionCustomVisualHandler {
            private bool _contentInitialized;
            internal IGlContext? _gl;
            private PixelSize _lastSize;
            private PixelSize _ownViewportSize; // Store the viewport size specific to this instance
            private PixelPoint _screenPosition = new PixelPoint(-1, -1); // Viewport position relative to top-level window

            private readonly Base3DViewport _parent;
            private readonly string _instanceId; // Unique identifier for this GlVisual instance

            public DateTime LastRenderTime { get; private set; } = DateTime.MinValue;
            public GL? SilkGl { get; private set; }

            public GlVisual(Base3DViewport parent) {
                _parent = parent;
                _instanceId = Guid.NewGuid().ToString("N")[..8]; // Short unique ID for debugging
            }

            public override void OnRender(ImmediateDrawingContext drawingContext) {
                try {
                    var frameTime = CalculateFrameTime();
                    RegisterForNextAnimationFrameUpdate();

                    var bounds = GetRenderBounds();
                    var size = PixelSize.FromSize(bounds.Size, 1);

                    if (size.Width < 1 || size.Height < 1) {
                        _parent._logger.LogTrace("OnRender: size too small {Width}x{Height}", size.Width, size.Height);
                        return;
                    }

                    // Declare controlSize outside the using block to ensure proper scope
                    PixelSize controlSize = default;
                    bool shouldUpdate = false;

                    if (drawingContext.TryGetFeature<ISkiaSharpApiLeaseFeature>(out var skiaFeature)) {
                        using var skiaLease = skiaFeature.Lease();
                        _ = skiaLease.GrContext ?? throw new Exception("Unable to get GrContext");
                        PixelSize canvasSize = default;

                        // Calculate control size in physical pixels using the top-level scaling
                        var topLevel = TopLevel.GetTopLevel(_parent);
                        var scaling = topLevel?.RenderScaling ?? 1.0;
                        controlSize = new PixelSize((int)(_parent._viewport!.Bounds.Width * scaling), (int)(_parent._viewport!.Bounds.Height * scaling));

                        using (var platformApiLease = skiaLease.TryLeasePlatformGraphicsApi()) {
                            if (platformApiLease?.Context is not IGlContext glContext)
                                throw new Exception("Unable to get IGlContext");

                            if (_gl != glContext) {
                                _contentInitialized = false;
                                _gl = glContext;

                                var gl = GL.GetApi(glContext.GlInterface.GetProcAddress);
                                SilkGl = gl;

                                // If this is a RenderView, register the context as master if not already set
                                if (_parent is RenderView renderView) {
                                    var sharedContextManager = RenderView.SharedContextManager;
                                    if (sharedContextManager.GetMasterContext().context == null) {
                                        sharedContextManager.SetMasterContext(glContext, gl);
                                    }
                                }

                                _parent._logger.LogDebug("New OpenGL context assigned to viewport: {ContextHashCode}", glContext.GetHashCode());
                            }

                            if (SilkGl == null) throw new Exception("Unable to get SilkGl");

                            // Use the control size to calculate canvas size for initialization
                            canvasSize = controlSize;

                            if (canvasSize.Width < 1 || canvasSize.Height < 1) {
                                _parent._logger.LogTrace(
                                    "OnRender: canvas size too small {Width}x{Height}", canvasSize.Width,
                                    canvasSize.Height);
                                return;
                            }

                            if (!_contentInitialized) {
                                _parent.OnGlInitInternal(SilkGl, canvasSize);
                                _contentInitialized = true;

                                string openGLVersion = SilkGl.GetStringS(StringName.Version);
                                string vendor = SilkGl.GetStringS(StringName.Vendor);
                                string renderer = SilkGl.GetStringS(StringName.Renderer);
                                _parent._logger.LogInformation("OpenGL Version: {Version} // {Vendor} // {Renderer}",
                                    openGLVersion, vendor, renderer);
                            }

                            // save current framebuffer
                            SilkGl.GetInteger(GetPName.DrawFramebufferBinding, out int oldFramebufferBinding);


                            if (_lastSize.Width != controlSize.Width || _lastSize.Height != controlSize.Height) {
                                _parent.OnGlResizeInternal(controlSize);
                                shouldUpdate = true;

                                // Update the viewport dimensions in the context manager for this specific context
                                // This ensures each RenderView maintains independent viewport state and only resizes
                                // with its own dimensions, preventing shared viewport state between windows
                                if (_gl != null) {
                                    RenderView.SharedContextManager.SetViewportDimensions(_gl, controlSize.Width, controlSize.Height);
                                }

                                // Also update our own instance-specific viewport size
                                _ownViewportSize = controlSize;
                            }

                            // Save current OpenGL state to ensure isolation between render views
                            var originalViewport = new int[4];
                            SilkGl.GetInteger(GetPName.Viewport, originalViewport);
                            var originalDepthTest = SilkGl.IsEnabled(EnableCap.DepthTest);
                            var originalCullFace = SilkGl.IsEnabled(EnableCap.CullFace);
                            SilkGl.GetInteger(GetPName.CullFaceMode, out int originalCullFaceMode);
                            SilkGl.GetInteger(GetPName.FrontFace, out int originalFrontFace);
                            var originalBlend = SilkGl.IsEnabled(EnableCap.Blend);
                            SilkGl.GetInteger(GetPName.BlendSrcRgb, out int originalBlendSrc);
                            SilkGl.GetInteger(GetPName.BlendDstRgb, out int originalBlendDst);
                            SilkGl.GetInteger(GetPName.BlendEquationRgb, out int originalBlendEquation);
                            var originalScissor = SilkGl.IsEnabled(EnableCap.ScissorTest);
                            var originalScissorBox = new int[4];
                            SilkGl.GetInteger(GetPName.ScissorBox, originalScissorBox);

                            SilkGl.Enable(EnableCap.DepthTest);
                            SilkGl.DepthFunc(DepthFunction.Less);

                            SilkGl.Enable(EnableCap.CullFace);
                            SilkGl.CullFace(TriangleFace.Back);
                            SilkGl.FrontFace(FrontFaceDirection.Ccw);

                            SilkGl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                            SilkGl.BlendEquation(BlendEquationModeEXT.FuncAdd);

                            // Use the control size for the viewport, not the canvas size from GL context
                            // This ensures that rendering happens with the correct dimensions for this specific control
                            SilkGl.Viewport(0, 0, (uint)controlSize.Width, (uint)controlSize.Height);

                            // Disable scissor test for FBO rendering to ensure we draw the full viewport
                            // This prevents Avalonia's UI clipping from affecting our internal render target
                            SilkGl.Disable(EnableCap.ScissorTest);

                            _parent.OnGlRenderInternal(frameTime);

                            // Restore the original OpenGL state to ensure isolation between render views
                            SilkGl.Viewport(originalViewport[0], originalViewport[1], (uint)originalViewport[2], (uint)originalViewport[3]);

                            if (originalScissor) SilkGl.Enable(EnableCap.ScissorTest); else SilkGl.Disable(EnableCap.ScissorTest);
                            SilkGl.Scissor(originalScissorBox[0], originalScissorBox[1], (uint)originalScissorBox[2], (uint)originalScissorBox[3]);

                            if (originalDepthTest) SilkGl.Enable(EnableCap.DepthTest); else SilkGl.Disable(EnableCap.DepthTest);
                            if (originalCullFace) SilkGl.Enable(EnableCap.CullFace); else SilkGl.Disable(EnableCap.CullFace);
                            SilkGl.CullFace((TriangleFace)originalCullFaceMode);
                            SilkGl.FrontFace((FrontFaceDirection)originalFrontFace);

                            if (originalBlend) SilkGl.Enable(EnableCap.Blend); else SilkGl.Disable(EnableCap.Blend);
                            SilkGl.BlendFunc((BlendingFactor)originalBlendSrc, (BlendingFactor)originalBlendDst);
                            SilkGl.BlendEquation((BlendEquationModeEXT)originalBlendEquation);

                            // restore old framebuffer
                            SilkGl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)oldFramebufferBinding);

                            // render 3d viewport texture
                            if (_parent.RenderTarget?.Framebuffer is not null &&
                                _parent.RenderTarget?.Texture is not null) {
                                var textureFB = (uint)_parent.RenderTarget.Framebuffer.NativeHandle;
                                SilkGl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, textureFB);
                                SilkGl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, (uint)oldFramebufferBinding);

                                var srcWidth = _parent.RenderTarget.Texture.Width;
                                var srcHeight = _parent.RenderTarget.Texture.Height;

                                // Determine the destination viewport for blitting based on the current context
                                // This ensures each window uses its own viewport dimensions rather than the shared state
                                int destW = controlSize.Width;
                                int destH = controlSize.Height;

                                if (_screenPosition.X >= 0) {
                                    // The destination Y is flipped because OpenGL's origin is bottom-left
                                    // and Avalonia's origin is top-left.
                                    // We take the full window height (originalViewport[3]) and subtract the top-left Y + height.
                                    int destX = _screenPosition.X;
                                    int destY = originalViewport[3] - (_screenPosition.Y + destH);

                                    var scissorEnabled = SilkGl.IsEnabled(EnableCap.ScissorTest);
                                    if (scissorEnabled) SilkGl.Disable(EnableCap.ScissorTest);

                                    // Use the context-specific viewport dimensions for blitting
                                    SilkGl.BlitFramebuffer(
                                        0, 0, srcWidth, srcHeight,
                                        destX, destY, destX + destW, destY + destH,
                                        ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Linear
                                    );

                                    if (scissorEnabled) SilkGl.Enable(EnableCap.ScissorTest);
                                }
                            }

                            // restore old framebuffer
                            SilkGl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)oldFramebufferBinding);
                        }

                        if (shouldUpdate) {
                            _lastSize = controlSize;
                        }
                    }
                    else {
                        throw new Exception("Unable to get ISkiaSharpApiLeaseFeature");
                    }
                }
                catch (Exception ex) {
                    _parent._logger.LogError(ex, "Render error");
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
                else if (message is PositionMessage posMsg) {
                    _screenPosition = posMsg.Position;
                }

                base.OnMessage(message);
            }

            private void DisposeResources() {
                if (_gl == null) return;

                try {
                    if (_contentInitialized) {
                        using (_gl.MakeCurrent()) {
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