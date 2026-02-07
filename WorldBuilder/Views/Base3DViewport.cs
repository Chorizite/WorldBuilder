using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Chorizite.Core.Render;
using Chorizite.OpenGLSDLBackend;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Views {
    public abstract partial class Base3DViewport : UserControl {
        private Control? _viewport;
        private GlVisual? _glVisual;
        private CompositionCustomVisual? _visual;
        private Size _lastViewportSize;

        private PixelSize _renderSize;
        protected ILogger _logger = NullLogger.Instance;

        public RenderTarget? RenderTarget { get; protected set; }
        public OpenGLRenderer? Renderer { get; private set; }

        protected Base3DViewport() {
            this.AttachedToVisualTree += OnAttachedToVisualTree;
        }

        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
            // Focus the control when attached, but only if no other control has focus
            if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() == null) {
                this.Focus();
            }
        }

        protected void InitializeBase3DView() {
            Focusable = true;

            _viewport = this.FindControl<Control>("Viewport") ??
                        throw new InvalidOperationException("Viewport control not found");
            _viewport.AttachedToVisualTree += ViewportAttachedToVisualTree;
            _viewport.DetachedFromVisualTree += ViewportDetachedFromVisualTree;
        }

        protected virtual void OnGlInitInternal(GL gl, PixelSize size) {
            _logger = new ColorConsoleLogger(GetType().Name, () => new ColorConsoleLoggerConfiguration());
            Renderer = new OpenGLRenderer(gl, _logger, null!, size.Width, size.Height);
            _renderSize = size;
            OnGlInit(gl, size);
        }

        protected virtual void OnGlResizeInternal(PixelSize size) {
            _renderSize = size;
            Renderer?.Resize(size.Width, size.Height);

            if (_viewport != null && _viewport.Bounds.Width > 0 && _viewport.Bounds.Height > 0) {
                var bounds = _viewport.Bounds;
                InputScale = new Vector2((float)(size.Width / bounds.Width), (float)(size.Height / bounds.Height));
            }

            OnGlResize(size);
        }

        protected virtual void OnGlRenderInternal(double frameTime) {
            if (_renderSize.Width <= 0 || _renderSize.Height <= 0) return;

            if (Renderer == null) return;

            // Ensure the renderer's main viewport matches this control's size
            Renderer.Resize(_renderSize.Width, _renderSize.Height);

            if (RenderTarget == null || RenderTarget.Texture.Width != _renderSize.Width ||
                RenderTarget.Texture.Height != _renderSize.Height) {
                RenderTarget?.Dispose();
                // Create a render target that matches this specific viewport's size
                RenderTarget = Renderer?.CreateRenderTarget(_renderSize.Width, _renderSize.Height);
            }

            if (RenderTarget == null) return;

            // Bind to our own render target for rendering
            Renderer!.BindRenderTarget(RenderTarget);

            // Set the viewport for the current render target size before rendering
            // This ensures the scene is rendered at the correct resolution for this viewport
            Renderer.GraphicsDevice.GL.Viewport(0, 0, (uint)_renderSize.Width, (uint)_renderSize.Height);

            OnGlRender(frameTime);
            Renderer.BindRenderTarget(null);
        }

        protected virtual void OnGlDestroyInternal() {
            OnGlDestroy();
            RenderTarget?.Dispose();
            RenderTarget = null;
        }

        protected abstract void OnGlInit(GL gl, PixelSize canvasSize);
        protected abstract void OnGlRender(double frameTime);
        protected abstract void OnGlResize(PixelSize canvasSize);
        protected abstract void OnGlDestroy();

        #region Input Event Handlers

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if (ShouldProcessKeyboardInput()) {
                OnGlKeyDown(e);
            }
        }

        protected abstract void OnGlKeyDown(KeyEventArgs e);

        protected override void OnKeyUp(KeyEventArgs e) {
            base.OnKeyUp(e);
            if (ShouldProcessKeyboardInput()) {
                OnGlKeyUp(e);
            }
        }

        protected abstract void OnGlKeyUp(KeyEventArgs e);

        protected override void OnPointerEntered(PointerEventArgs e) {
            base.OnPointerEntered(e);
        }

        protected override void OnPointerExited(PointerEventArgs e) {
            base.OnPointerExited(e);
        }

        protected override void OnPointerMoved(PointerEventArgs e) {
            base.OnPointerMoved(e);
            var pos = e.GetPosition(this);
            var scaledPos = new Vector2((float)pos.X, (float)pos.Y) * InputScale;
            OnGlPointerMoved(e, scaledPos);
        }

        protected abstract void OnGlPointerMoved(PointerEventArgs e, Vector2 mousePositionScaled);

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e) {
            base.OnPointerWheelChanged(e);
            OnGlPointerWheelChanged(e);
        }

        protected abstract void OnGlPointerWheelChanged(PointerWheelEventArgs e);

        protected override void OnPointerPressed(PointerPressedEventArgs e) {
            base.OnPointerPressed(e);
            OnGlPointerPressed(e);
        }

        protected abstract void OnGlPointerPressed(PointerPressedEventArgs e);

        protected override void OnPointerReleased(PointerReleasedEventArgs e) {
            base.OnPointerReleased(e);
            OnGlPointerReleased(e);
        }

        protected abstract void OnGlPointerReleased(PointerReleasedEventArgs e);

        /// <summary>
        /// Determines if keyboard input should be processed for this control
        /// Only processes if focused and the focused element is not a text input control
        /// </summary>
        private bool ShouldProcessKeyboardInput() {
            if (!IsEffectivelyVisible || !IsEnabled || !IsFocused) {
                return false;
            }

            // Check if focus is on a text input control that needs keyboard input
            if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Control focusedElement &&
                focusedElement != this) {
                // Only block keyboard input if it's a control that needs text input
                if (focusedElement is TextBox || focusedElement is ComboBox) {
                    return false;
                }
            }

            return true;
        }

        #endregion

        #region Visual Tree Management

        private void ViewportAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
            if (_viewport == null) return;
            var visual = ElementComposition.GetElementVisual(_viewport);
            if (visual == null) return;

            _glVisual = new GlVisual(this);
            _visual = visual.Compositor.CreateCustomVisual(_glVisual);
            ElementComposition.SetElementChildVisual(_viewport, _visual);
            _logger.LogInformation("Attached to visual tree");
            // Update size immediately
            UpdateSize(_viewport.Bounds.Size);
        }

        private void ViewportDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
            _visual?.SendHandlerMessage(new DisposeMessage());
            _visual = null;
            if (_viewport != null) {
                ElementComposition.SetElementChildVisual(_viewport, null);
            }
        }

        protected override Size ArrangeOverride(Size finalSize) {
            var size = base.ArrangeOverride(finalSize);

            // After arrangement, get the actual viewport size
            if (_viewport != null && _visual != null) {
                var viewportSize = _viewport.Bounds.Size;
                // Only update if the size actually changed
                if (Math.Abs(viewportSize.Width - _lastViewportSize.Width) > 0.5 ||
                    Math.Abs(viewportSize.Height - _lastViewportSize.Height) > 0.5) {
                    _lastViewportSize = viewportSize;
                    UpdateSize(viewportSize);

                    // Update render size when layout changes to ensure proper scaling
                    if (_renderSize.Width != (int)viewportSize.Width || _renderSize.Height != (int)viewportSize.Height) {
                        _renderSize = new PixelSize((int)viewportSize.Width, (int)viewportSize.Height);
                        Renderer?.Resize((int)viewportSize.Width, (int)viewportSize.Height);
                    }
                }
            }

            return size;
        }

        private void UpdateSize(Size size) {
            if (_visual != null && size.Width > 0 && size.Height > 0) {
                _visual.Size = new Avalonia.Vector(size.Width, size.Height);
            }
        }

        #endregion

        #region GlVisual Class

        private class GlVisual : CompositionCustomVisualHandler {
            private bool _contentInitialized;
            internal IGlContext? _gl;
            private PixelSize _lastSize;
            private PixelSize _ownViewportSize; // Store the viewport size specific to this instance

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

                        // Calculate control size before the using block to ensure it's accessible later
                        controlSize = new PixelSize((int)_parent._viewport!.Bounds.Width, (int)_parent._viewport!.Bounds.Height);

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

                                // Log when a new context is assigned to this viewport for debugging
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


                            // Use the actual control size instead of the GL viewport size for resize detection
                            // This ensures each window resizes independently

                            if (_lastSize.Width != controlSize.Width || _lastSize.Height != controlSize.Height) {
                                _parent.OnGlResizeInternal(controlSize);
                                shouldUpdate = true;

                                // Update the viewport dimensions in the context manager for this specific context
                                // This ensures each RenderView maintains independent viewport state and only resizes
                                // with its own dimensions, preventing shared viewport state between windows
                                if (_gl != null) {
                                    RenderView.SharedContextManager.SetViewportDimensions(_gl, controlSize.Width, controlSize.Height);

                                    // Log the viewport dimensions being set for debugging
                                    _parent._logger.LogInformation("Setting viewport dimensions for context: {Width}x{Height}",
                                        controlSize.Width, controlSize.Height);
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
                            _parent.OnGlRenderInternal(frameTime);

                            // Restore the original OpenGL state to ensure isolation between render views
                            SilkGl.Viewport(originalViewport[0], originalViewport[1], (uint)originalViewport[2], (uint)originalViewport[3]);

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
                                int destX = 0, destY = 0;
                                int destW, destH;

                                // Prioritize our own instance-specific viewport size over the shared context manager
                                destW = _ownViewportSize.Width;
                                destH = _ownViewportSize.Height;

                                // Disable scissor test for blit to ensure we draw the full viewport
                                // This is necessary because the scissor state might be leaked/persisted from the Main Window
                                // in the shared context, causing clipping of the Debug Window's content.
                                var scissorEnabled = SilkGl.IsEnabled(EnableCap.ScissorTest);
                                if (scissorEnabled) SilkGl.Disable(EnableCap.ScissorTest);

                                // Use the context-specific viewport dimensions for blitting
                                SilkGl.BlitFramebuffer(
                                    0, 0, srcWidth, srcHeight,
                                    destX, destY, destW, destH,
                                    ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Linear
                                );

                                if (scissorEnabled) SilkGl.Enable(EnableCap.ScissorTest);
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

        public Vector2 InputScale { get; private set; }

        #endregion

        public class DisposeMessage {
        }
    }
}
