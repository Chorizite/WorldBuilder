using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.Raw;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Raylib_cs;
using SkiaSharp;

namespace WorldBuilder.Lib.Avalonia {
    internal sealed class RaylibTopLevelImpl : ITopLevelImpl {
        private readonly RaylibPlatformGraphics _platformGraphics;
        private readonly IClipboard _clipboard;
        private readonly TouchDevice _touchDevice = new();
        internal RaylibSkiaSurface? _surface;
        private WindowTransparencyLevel _transparencyLevel = WindowTransparencyLevel.Transparent;
        private PixelSize _renderSize;
        internal IInputRoot? _inputRoot;
        private RaylibStandardCursorImpl? _cursorShape;
        private bool _isDisposed;
        private RenderTexture2D? _renderTarget;

        public double RenderScaling { get; private set; } = 1.0;
        double ITopLevelImpl.DesktopScaling => 1.0;
        public Compositor Compositor { get; }
        public Size ClientSize { get; private set; }

        public WindowTransparencyLevel TransparencyLevel {
            get => _transparencyLevel;
            private set {
                if (_transparencyLevel.Equals(value)) return;
                _transparencyLevel = value;
                TransparencyLevelChanged?.Invoke(value);
                Console.WriteLine($"Transparency level changed to: {_transparencyLevel}");
            }
        }

        public Action<Rect>? Paint { get; set; }
        public Action<Size, WindowResizeReason>? Resized { get; set; }
        public Action? Closed { get; set; }
        public Action<RawInputEventArgs>? Input { get; set; }
        public Action? LostFocus { get; set; }
        public Action<RaylibStandardCursorImpl>? CursorChanged { get; set; }
        public Action<double>? ScalingChanged { get; set; }
        public Action<WindowTransparencyLevel>? TransparencyLevelChanged { get; set; }

        IEnumerable<object> ITopLevelImpl.Surfaces => GetOrCreateSurfaces();

        AcrylicPlatformCompensationLevels ITopLevelImpl.AcrylicCompensationLevels
            => new(1.0, 1.0, 1.0);

        public IPlatformHandle? Handle => null;

        public RaylibTopLevelImpl(RenderTexture2D renderTarget, RaylibPlatformGraphics platformGraphics, IClipboard clipboard, Compositor compositor) {
            _renderTarget = renderTarget;
            _platformGraphics = platformGraphics;
            _clipboard = clipboard;
            Compositor = compositor;
            _platformGraphics.AddRef();
        }

        private RaylibSkiaSurface CreateSurface() {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(RaylibTopLevelImpl));

            return _platformGraphics.GetSharedContext().CreateSurface(_renderSize, RenderScaling);
        }

        public RaylibSkiaSurface? TryGetSurface() => _surface;

        public RaylibSkiaSurface GetOrCreateSurface()
            => _surface ??= CreateSurface();

        private IEnumerable<object> GetOrCreateSurfaces()
            => new object[] { GetOrCreateSurface() };

        public void UpdateRenderTarget(RenderTexture2D renderTarget) {
            _renderTarget = renderTarget;
            Console.WriteLine("Render target updated");
        }

        public void SetRenderSize(PixelSize renderSize, double renderScaling) {
            var hasScalingChanged = RenderScaling != renderScaling;
            if (_renderSize == renderSize && !hasScalingChanged)
                return;

            var oldClientSize = ClientSize;
            var unclampedClientSize = renderSize.ToSize(renderScaling);

            ClientSize = new Size(Math.Max(unclampedClientSize.Width, 0.0), Math.Max(unclampedClientSize.Height, 0.0));
            RenderScaling = renderScaling;

            if (_renderSize != renderSize) {
                _renderSize = renderSize;

                if (_surface is not null) {
                    _surface.Dispose();
                    _surface = null;
                }

                if (_isDisposed)
                    return;

                _surface = CreateSurface();
            }

            if (hasScalingChanged) {
                if (_surface != null)
                    _surface.RenderScaling = RenderScaling;
                ScalingChanged?.Invoke(RenderScaling);
                Console.WriteLine($"Render scaling changed to: {RenderScaling}");
            }

            if (oldClientSize != ClientSize) {
                Resized?.Invoke(ClientSize, hasScalingChanged ? WindowResizeReason.DpiChange : WindowResizeReason.Unspecified);
                Console.WriteLine($"Client size changed to: {ClientSize}");
            }
        }

        public void OnDraw(Rect rect) {
            Paint?.Invoke(rect);
        }

        public void Render() {
            if (_surface is null || _renderTarget is null)
                return;
            
            Raylib.BeginTextureMode(_renderTarget.Value);
            Paint?.Invoke(new Rect(0, 0, ClientSize.Width, ClientSize.Height));
            Raylib.EndTextureMode();
        }

        public void OnLostFocus() {
            LostFocus?.Invoke();
            Console.WriteLine("Focus lost");
        }

        void ITopLevelImpl.SetInputRoot(IInputRoot inputRoot)
            => _inputRoot = inputRoot;

        Point ITopLevelImpl.PointToClient(PixelPoint point)
            => point.ToPoint(RenderScaling);

        PixelPoint ITopLevelImpl.PointToScreen(Point point)
            => PixelPoint.FromPoint(point, RenderScaling);

        void ITopLevelImpl.SetCursor(ICursorImpl? cursor) {
            var cursorShape = (cursor as RaylibStandardCursorImpl);
            if (_cursorShape == cursorShape)
                return;

            _cursorShape = cursorShape;
            CursorChanged?.Invoke(cursorShape);
            Console.WriteLine($"Cursor changed to: {cursorShape?.ToString() ?? "null"}");
        }

        IPopupImpl? ITopLevelImpl.CreatePopup() => null;

        void ITopLevelImpl.SetTransparencyLevelHint(IReadOnlyList<WindowTransparencyLevel> transparencyLevels) {
            foreach (var transparencyLevel in transparencyLevels) {
                if (transparencyLevel == WindowTransparencyLevel.Transparent || transparencyLevel == WindowTransparencyLevel.None) {
                    TransparencyLevel = transparencyLevel;
                    return;
                }
            }
        }

        void ITopLevelImpl.SetFrameThemeVariant(PlatformThemeVariant themeVariant) {
        }

        object? IOptionalFeatureProvider.TryGetFeature(Type featureType) {
            if (featureType == typeof(IClipboard))
                return _clipboard;
            return null;
        }

        public void Dispose() {
            if (_isDisposed)
                return;

            _isDisposed = true;

            if (_surface is not null) {
                _surface.Dispose();
                _surface = null;
            }

            if (_renderTarget is not null) {
                Raylib.UnloadRenderTexture(_renderTarget.Value);
                _renderTarget = null;
            }

            Closed?.Invoke();
            _platformGraphics.Release();
        }
    }

}