using Autofac.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Chorizite.Core.Render;
using Chorizite.OpenGLSDLBackend;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using SkiaSharp;
using System;
using System.IO;
using System.Numerics;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Lib.Extensions;
using WorldBuilder.Shared.Models;
using static Avalonia.OpenGL.GlConsts;

namespace WorldBuilder.Views {
    public abstract partial class Base3DView : UserControl {
        private Control? _viewport;
        private GlVisual? _glVisual;
        private CompositionCustomVisual? _visual;
        public AvaloniaInputState InputState { get; } = new();
        private bool _isPointerOverViewport;
        private Vector2 _lastMousePosition;
        private Size _lastViewportSize;

        public RenderTarget? RenderTarget { get; protected set; }
        public OpenGLRenderer? Renderer { get; private set; }

        private PixelSize _renderSize;

        protected Base3DView() {
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
            Background = Brushes.Transparent;

            _viewport = this.FindControl<Control>("Viewport") ?? throw new InvalidOperationException("Viewport control not found");
            _viewport.AttachedToVisualTree += ViewportAttachedToVisualTree;
            _viewport.DetachedFromVisualTree += ViewportDetachedFromVisualTree;

            // Add pointer tracking to viewport
            _viewport.PointerEntered += ViewportPointerEntered;
            _viewport.PointerExited += ViewportPointerExited;
        }

        private void ViewportPointerEntered(object? sender, PointerEventArgs e) {
            _isPointerOverViewport = true;
        }

        private void ViewportPointerExited(object? sender, PointerEventArgs e) {
            _isPointerOverViewport = false;
        }

        #region Input Event Handlers

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);

            // Only process keyboard input if focused and not interacting with other controls
            if (!ShouldProcessKeyboardInput()) {
                return;
            }

            try {
                InputState.Modifiers = e.KeyModifiers;
                InputState.SetKey(e.Key, true);
                OnGlKeyDown(e);
                e.Handled = true; // Mark as handled to prevent bubbling
            }
            catch (Exception ex) {
                Console.WriteLine($"Error in OnKeyDown: {ex}");
            }
        }

        protected abstract void OnGlKeyDown(KeyEventArgs e);

        protected override void OnKeyUp(KeyEventArgs e) {
            base.OnKeyUp(e);

            var hadKeyDown = InputState.IsKeyDown(e.Key);
            InputState.Modifiers = e.KeyModifiers;
            InputState.SetKey(e.Key, false);

            // Only call the GL event if we're focused and had the key down
            if (hadKeyDown && ShouldProcessKeyboardInput()) {
                try {
                    OnGlKeyUp(e);
                    e.Handled = true;
                }
                catch (Exception ex) {
                    Console.WriteLine($"Error in OnKeyUp: {ex}");
                }
            }
        }

        protected abstract void OnGlKeyUp(KeyEventArgs e);

        protected override void OnPointerEntered(PointerEventArgs e) {
            base.OnPointerEntered(e);
            // We track viewport entry separately now
        }

        protected override void OnPointerExited(PointerEventArgs e) {
            base.OnPointerExited(e);
            // We track viewport exit separately now
        }

        protected override void OnPointerMoved(PointerEventArgs e) {
            base.OnPointerMoved(e);

            // Check if pointer is over viewport
            if (!_isPointerOverViewport || !IsEffectivelyVisible || !IsEnabled) return;

            try {
                var position = e.GetPosition(_viewport);
                InputState.Modifiers = e.KeyModifiers;
                UpdateMouseState(position, e.Properties);
                _lastMousePosition = new Vector2((float)position.X, (float)position.Y);
                var scaledPosition = new Vector2((float)position.X * InputScale.X, (float)position.Y * InputScale.Y);
                OnGlPointerMoved(e, scaledPosition);
            }
            catch (Exception ex) {
                Console.WriteLine($"Error in OnPointerMoved: {ex}");
            }
        }

        protected abstract void OnGlPointerMoved(PointerEventArgs e, Vector2 mousePositionScaled);

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e) {
            base.OnPointerWheelChanged(e);

            // Check if pointer is over viewport
            if (!_isPointerOverViewport || !IsEffectivelyVisible || !IsEnabled) return;

            try {
                var position = e.GetPosition(_viewport);
                InputState.Modifiers = e.KeyModifiers;
                UpdateMouseState(position, e.Properties);
                _lastMousePosition = new Vector2((float)position.X, (float)position.Y);

                OnGlPointerWheelChanged(e);
                e.Handled = true; // Prevent scrolling parent controls
            }
            catch (Exception ex) {
                Console.WriteLine($"Error in OnPointerWheelChanged: {ex}");
            }
        }

        protected abstract void OnGlPointerWheelChanged(PointerWheelEventArgs e);

        protected override void OnPointerPressed(PointerPressedEventArgs e) {
            base.OnPointerPressed(e);

            // Check if pointer is over viewport before processing
            if (_isPointerOverViewport && IsEffectivelyVisible && IsEnabled) {
                // Request focus when clicked on viewport
                this.Focus();

                try {
                    var position = e.GetPosition(_viewport);
                    InputState.Modifiers = e.KeyModifiers;
                    UpdateMouseState(position, e.Properties);

                    OnGlPointerPressed(e);
                    e.Handled = true;
                }
                catch (Exception ex) {
                    Console.WriteLine($"Error in OnPointerPressed: {ex}");
                }
            }
        }

        protected abstract void OnGlPointerPressed(PointerPressedEventArgs e);

        protected override void OnPointerReleased(PointerReleasedEventArgs e) {
            base.OnPointerReleased(e);

            // Check if pointer is over viewport
            if (!_isPointerOverViewport || !IsEffectivelyVisible || !IsEnabled) return;

            try {
                var position = e.GetPosition(_viewport);
                InputState.Modifiers = e.KeyModifiers;
                UpdateMouseState(position, e.Properties);

                OnGlPointerReleased(e);
                e.Handled = true;
            }
            catch (Exception ex) {
                Console.WriteLine($"Error in OnPointerReleased: {ex}");
            }
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
            var focusedElement = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
            if (focusedElement != null && focusedElement != this) {
                // Only block keyboard input if it's a control that needs text input
                if (focusedElement is TextBox || focusedElement is ComboBox) {
                    return false;
                }
            }

            return true;
        }

        #endregion

        #region Helper Methods

        protected virtual void UpdateMouseState(Point position, PointerPointProperties properties) {
        }

        public bool HitTest(Point point) => Bounds.Contains(point);

        #endregion

        #region Visual Tree Management

        private void ViewportAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
            if (_viewport == null) return;
            var visual = ElementComposition.GetElementVisual(_viewport);
            if (visual == null) return;

            _glVisual = new GlVisual(this);
            _visual = visual.Compositor.CreateCustomVisual(_glVisual);
            ElementComposition.SetElementChildVisual(_viewport, _visual);

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

        protected virtual void OnGlInitInternal(GL gl, PixelSize size) {
            var log = new ColorConsoleLogger("OpenGLRenderer", () => new ColorConsoleLoggerConfiguration());
            Renderer = new OpenGLRenderer(gl, log, null!, size.Width, size.Height);
            _renderSize = size;
            OnGlInit(gl, size);
        }

        protected virtual void OnGlResizeInternal(PixelSize size) {
            _renderSize = size;
            Renderer?.Resize(size.Width, size.Height);
            OnGlResize(size);
        }

        protected virtual void OnGlRenderInternal(double frameTime) {

            if (_renderSize.Width <= 0 || _renderSize.Height <= 0) return;

            if (RenderTarget == null || RenderTarget.Texture.Width != _renderSize.Width || RenderTarget.Texture.Height != _renderSize.Height) {
                RenderTarget?.Dispose();
                RenderTarget = Renderer?.CreateRenderTarget(_renderSize.Width, _renderSize.Height);
            }

            if (RenderTarget == null || Renderer == null) return;
            Renderer.BindRenderTarget(RenderTarget);

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

        #region GlVisual Class
        private class GlVisual : CompositionCustomVisualHandler {
            private bool _contentInitialized;
            internal IGlContext? _gl;
            private PixelSize _lastSize;

            private AvaloniaInputState _inputState => _parent.InputState;
            private readonly Base3DView _parent;

            public DateTime LastRenderTime { get; private set; } = DateTime.MinValue;
            public GL? SilkGl { get; private set; }

            public GlVisual(Base3DView parent) {
                _parent = parent;
            }

            public override void OnRender(ImmediateDrawingContext drawingContext) {
                try {
                    var frameTime = CalculateFrameTime();
                    _inputState.OnFrame();
                    RegisterForNextAnimationFrameUpdate();

                    var bounds = GetRenderBounds();
                    var size = PixelSize.FromSize(bounds.Size, 1);

                    if (size.Width < 1 || size.Height < 1) return;

                    if (drawingContext.TryGetFeature<ISkiaSharpApiLeaseFeature>(out var skiaFeature)) {
                        using var skiaLease = skiaFeature.Lease();
                        var grContext = skiaLease.GrContext;
                        if (grContext == null) return;

                        var dst = skiaLease.SkCanvas.DeviceClipBounds;
                        var canvasSize = new PixelSize(dst.Width, dst.Height);

                        using (var platformApiLease = skiaLease.TryLeasePlatformGraphicsApi()) {
                            if (platformApiLease?.Context is not IGlContext glContext) return;

                            if (_gl != glContext) {
                                _contentInitialized = false;
                                _gl = glContext;
                            }

                            var gl = GL.GetApi(glContext.GlInterface.GetProcAddress);
                            SilkGl = gl;

                            _parent.InputScale = new Vector2(dst.Width / (float)size.Width, dst.Height / (float)size.Height);

                            glContext.GlInterface.GetIntegerv(GL_FRAMEBUFFER_BINDING, out var oldFb);
                            SetDefaultStates(gl);
                            if (!_contentInitialized) {
                                _parent.OnGlInitInternal(gl, canvasSize);
                                _contentInitialized = true;

                                string openGLVersion = gl.GetStringS(StringName.Version);
                                Console.WriteLine($"OpenGL Version: {openGLVersion}");

                                // You can also get the vendor and renderer information
                                string vendor = gl.GetStringS(StringName.Vendor);
                                string renderer = gl.GetStringS(StringName.Renderer);
                                Console.WriteLine($"OpenGL Vendor: {vendor}");
                                Console.WriteLine($"OpenGL Renderer: {renderer}");
                            }

                            if (_lastSize.Width != canvasSize.Width || _lastSize.Height != canvasSize.Height) {
                                _parent.OnGlResizeInternal(canvasSize);
                            }

                            gl.Viewport(0, 0, (uint)canvasSize.Width, (uint)canvasSize.Height);

                            _parent.OnGlRenderInternal(frameTime);
                            glContext.GlInterface.BindFramebuffer(GL_FRAMEBUFFER, oldFb);

                            if (_parent.RenderTarget?.Framebuffer is not null && _parent.RenderTarget?.Texture is not null) {
                                var textureFB = (int)_parent.RenderTarget.Framebuffer.NativeHandle;
                                glContext.GlInterface.BindFramebuffer(GL_READ_FRAMEBUFFER, textureFB);
                                glContext.GlInterface.BindFramebuffer(GL_DRAW_FRAMEBUFFER, oldFb);

                                var srcWidth = _parent.RenderTarget.Texture.Width;
                                var srcHeight = _parent.RenderTarget.Texture.Height;

                                glContext.GlInterface.BlitFramebuffer(
                                    0, 0, srcWidth, srcHeight,
                                    dst.Left, dst.Top, dst.Right, dst.Bottom,
                                    GL_COLOR_BUFFER_BIT,
                                    GL_LINEAR
                                );
                            }
                        }

                        _lastSize = canvasSize;
                    }
                }
                catch (Exception ex) {
                    Console.WriteLine($"Render error: {ex}");
                }
            }

            public static void SetDefaultStates(GL gl) {
                gl.Enable(EnableCap.DepthTest);
                gl.DepthFunc(DepthFunction.Greater);

                gl.Enable(EnableCap.CullFace);
                gl.CullFace(TriangleFace.Back);
                gl.FrontFace(FrontFaceDirection.Ccw);

                gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
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
                            _parent.OnGlDestroyInternal();
                            _contentInitialized = false;
                        }
                    }
                }
                catch (Exception ex) {
                    Console.WriteLine($"Error disposing resources: {ex}");
                }
                finally {
                    _gl = null;
                }
            }
        }

        public Vector2 InputScale { get; private set; }

        #endregion

        public class DisposeMessage { }
    }
}