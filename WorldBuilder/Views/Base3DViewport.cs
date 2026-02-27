using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Chorizite.Core.Render;
using Chorizite.OpenGLSDLBackend;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Views {
    public abstract partial class Base3DViewport : UserControl {
        protected Control? _viewport;
        private GlVisual? _glVisual;
        private CompositionCustomVisual? _visual;
        private Size _lastViewportSize;

        private PixelSize _renderSize;
        protected ILogger _logger = NullLogger.Instance;

        public RenderTarget? RenderTarget { get; protected set; }
        public OpenGLRenderer? Renderer { get; private set; }

        public static readonly StyledProperty<bool> IsTooltipProperty =
            AvaloniaProperty.Register<Base3DViewport, bool>(nameof(IsTooltip));

        public bool IsTooltip {
            get => GetValue(IsTooltipProperty);
            set => SetValue(IsTooltipProperty, value);
        }

        public static readonly StyledProperty<IBrush?> ClearColorProperty =
            AvaloniaProperty.Register<Base3DViewport, IBrush?>(nameof(ClearColor), new SolidColorBrush(Color.FromRgb(38, 38, 51)));

        public IBrush? ClearColor {
            get => GetValue(ClearColorProperty);
            set => SetValue(ClearColorProperty, value);
        }

        public Vector2 InputScale { get; private set; }

        protected Base3DViewport() {
            this.AttachedToVisualTree += OnAttachedToVisualTree;
            this.EffectiveViewportChanged += (s, e) => UpdateScreenPosition();
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
            LayoutUpdated += OnLayoutUpdated;
        }

        private void OnLayoutUpdated(object? sender, EventArgs e) {
            UpdateScreenPosition();
        }

        private void UpdateScreenPosition() {
            if (_viewport != null && _visual != null) {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel != null) {
                    var point = _viewport.TranslatePoint(new Point(0, 0), topLevel);
                    if (point.HasValue) {
                        var scaling = topLevel.RenderScaling;
                        var bounds = _viewport.Bounds;

                        if (bounds.Width > 0 && bounds.Height > 0) {
                            InputScale = new Vector2((float)scaling, (float)scaling);
                        }

                        // Calculate effective clip
                        var clipRect = new Rect(0, 0, bounds.Width, bounds.Height);
                        var current = _viewport.Parent;
                        while (current != null) {
                            if (current is Control control && control.ClipToBounds) {
                                var offset = _viewport.TranslatePoint(new Point(0, 0), control);
                                if (offset.HasValue) {
                                    var relativeClip = new Rect(-offset.Value.X, -offset.Value.Y, control.Bounds.Width, control.Bounds.Height);
                                    clipRect = clipRect.Intersect(relativeClip);
                                }
                            }
                            current = current.Parent;
                        }

                        _visual.SendHandlerMessage(new PositionMessage {
                            Position = new PixelPoint((int)(point.Value.X * scaling), (int)(point.Value.Y * scaling)),
                            Size = new PixelSize((int)(bounds.Width * scaling), (int)(bounds.Height * scaling)),
                            Scaling = scaling,
                            SurfaceHeight = (int)(topLevel.ClientSize.Height * scaling),
                            Clip = new PixelRect((int)(clipRect.X * scaling), (int)(clipRect.Y * scaling), (int)(clipRect.Width * scaling), (int)(clipRect.Height * scaling))
                        });
                    }
                }
            }
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
        }

        protected virtual void OnGlDestroyInternal() {
            OnGlDestroy();
            RenderTarget?.Dispose();
            RenderTarget = null;
            Renderer?.Dispose();
            Renderer = null;
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
            // Update size immediately
            UpdateSize(_viewport.Bounds.Size);
            UpdateScreenPosition();
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
                    UpdateScreenPosition();
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

        public class DisposeMessage {
        }

        public class PositionMessage {
            public PixelPoint Position { get; set; }
            public PixelSize Size { get; set; }
            public double Scaling { get; set; }
            public int SurfaceHeight { get; set; }
            public PixelRect Clip { get; set; }
        }
    }
}