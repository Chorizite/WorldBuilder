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
using WorldBuilder.Tools;
using static Avalonia.OpenGL.GlConsts;

namespace WorldBuilder.Views {
    public abstract partial class Base3DView : UserControl {
        private Control? _viewport;
        private GlVisual? _glVisual;
        private CompositionCustomVisual? _visual;
        public AvaloniaInputState InputState { get; } = new();
        public CameraManager CameraManager { get; }
        private bool _hasPointer;
        private Vector2 _lastMousePosition;
        private Size _lastViewportSize;

        public RenderTarget RenderTarget { get; protected set; }
        public OpenGLRenderer Renderer { get; private set; }

        private PixelSize _renderSize;

        protected Base3DView() {
            this.AttachedToVisualTree += (s, e) => this.Focus();
            CameraManager = new CameraManager(new OrthographicTopDownCamera(new()));
        }
        protected void InitializeBase3DView() {
            Focusable = true;
            Background = Brushes.Transparent;

            _viewport = this.FindControl<Control>("Viewport") ?? throw new InvalidOperationException("Viewport control not found");
            _viewport.AttachedToVisualTree += ViewportAttachedToVisualTree;
            _viewport.DetachedFromVisualTree += ViewportDetachedFromVisualTree;
            _hasPointer = true;
        }

        #region Input Event Handlers

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if (IsEffectivelyVisible && IsFocused) {
                InputState.Modifiers = e.KeyModifiers;
                InputState.SetKey(e.Key, true);
                OnGlKeyDown(e);
            }
        }

        protected abstract void OnGlKeyDown(KeyEventArgs e);

        protected override void OnKeyUp(KeyEventArgs e) {
            base.OnKeyUp(e);
            InputState.Modifiers = e.KeyModifiers;
            InputState.SetKey(e.Key, false);
            OnGlKeyUp(e);
        }

        protected abstract void OnGlKeyUp(KeyEventArgs e);

        protected override void OnPointerEntered(PointerEventArgs e) {
            base.OnPointerEntered(e);
            _hasPointer = true;
        }

        protected override void OnPointerExited(PointerEventArgs e) {
            base.OnPointerExited(e);
            _hasPointer = false;
        }

        protected override void OnPointerMoved(PointerEventArgs e) {
            if (!IsValidForInput()) return;

            var position = e.GetPosition(this);
            InputState.Modifiers = e.KeyModifiers;
            UpdateMouseState(position, e.Properties);
            _lastMousePosition = new Vector2((float)position.X, (float)position.Y);

            OnGlPointerMoved(e);
        }

        protected abstract void OnGlPointerMoved(PointerEventArgs e);

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e) {
            base.OnPointerWheelChanged(e);

            if (_glVisual != null) {
                var position = e.GetPosition(this);
                InputState.Modifiers = e.KeyModifiers;
                UpdateMouseState(position, e.Properties);
                _lastMousePosition = new Vector2((float)position.X, (float)position.Y);
            }

            OnGlPointerWheelChanged(e);
        }

        protected abstract void OnGlPointerWheelChanged(PointerWheelEventArgs e);
        private bool IsValidForInput() => IsEffectivelyVisible && _hasPointer && IsFocused;

        protected override void OnPointerPressed(PointerPressedEventArgs e) {
            base.OnPointerPressed(e);
            if (!IsValidForInput()) return;

            var position = e.GetPosition(this);
            InputState.Modifiers = e.KeyModifiers;
            UpdateMouseState(position, e.Properties);

            OnGlPointerPressed(e);
        }

        protected abstract void OnGlPointerPressed(PointerPressedEventArgs e);

        protected override void OnPointerReleased(PointerReleasedEventArgs e) {
            base.OnPointerReleased(e);
            if (!IsValidForInput()) return;

            var position = e.GetPosition(this);
            InputState.Modifiers = e.KeyModifiers;
            UpdateMouseState(position, e.Properties);

            OnGlPointerReleased(e);
        }

        protected abstract void OnGlPointerReleased(PointerReleasedEventArgs e);

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
            if (_visual != null)
                _visual.Size = new Avalonia.Vector(size.Width, size.Height);
        }
        #endregion

        protected virtual void OnGlInitInternal(GL gl, PixelSize size) {
            var log = new ColorConsoleLogger("OpenGLRenderer", () => new ColorConsoleLoggerConfiguration());
            Renderer = new OpenGLRenderer(gl, log, null, size.Width, size.Height);
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
                RenderTarget = Renderer.CreateRenderTarget(_renderSize.Width, _renderSize.Height);
            }

            Renderer.BindRenderTarget(RenderTarget);

            OnGlRender(frameTime);

            Renderer.BindRenderTarget(null);
        }

        protected virtual void OnGlDestroyInternal() {
            OnGlDestroy();
        }

        protected abstract void OnGlInit(GL gl, PixelSize canvasSize);
        protected abstract void OnGlRender(double frameTime);
        protected abstract void OnGlResize(PixelSize canvasSize);
        protected abstract void OnGlDestroy();

        #region GlVisual Class
        private class GlVisual : CompositionCustomVisualHandler {
            private OpenGLRenderer _renderer;
            private bool _contentInitialized;
            private IGlContext? _gl;
            private PixelSize _lastSize;

            private AvaloniaInputState _inputState => _parent.InputState;
            private readonly Base3DView _parent;

            public DateTime LastRenderTime { get; private set; } = DateTime.MinValue;

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
                        var canvasSize = new PixelSize((int)bounds.Width, (int)bounds.Height);

                        using (var platformApiLease = skiaLease.TryLeasePlatformGraphicsApi()) {
                            if (platformApiLease?.Context is not IGlContext glContext) return;

                            if (_gl != glContext) {
                                _contentInitialized = false;
                                _gl = glContext;
                            }

                            var gl = GL.GetApi(glContext.GlInterface.GetProcAddress);

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

        #endregion

        public class DisposeMessage { }
    }
}