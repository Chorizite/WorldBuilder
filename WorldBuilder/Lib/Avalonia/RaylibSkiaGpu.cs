using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Skia;
using Raylib_cs;
using SkiaSharp;

namespace WorldBuilder.Lib.Avalonia {
    internal sealed class RaylibSkiaGpu : ISkiaGpu {
        private readonly GRContext _grContext;
        public bool IsLost => _grContext.IsAbandoned;

        public RaylibSkiaGpu(GRContext grContext) {
            _grContext = grContext ?? throw new ArgumentNullException(nameof(grContext));
        }

        object? IOptionalFeatureProvider.TryGetFeature(Type featureType) => null;

        IDisposable IPlatformGraphicsContext.EnsureCurrent() => EmptyDisposable.Instance;

        ISkiaGpuRenderTarget? ISkiaGpu.TryCreateRenderTarget(IEnumerable<object> surfaces)
            => surfaces.OfType<RaylibSkiaSurface>().FirstOrDefault() is { } surface
                ? new RaylibSkiaRenderTarget(surface, _grContext)
                : null;

        public RaylibSkiaSurface CreateSurface(PixelSize size, double renderScaling) {
            size = new PixelSize(Math.Max(size.Width, 1), Math.Max(size.Height, 1));
            var texture = Raylib.LoadRenderTexture(size.Width, size.Height);
            if (texture.Id == 0) {
                throw new InvalidOperationException("Failed to create Raylib render texture");
            }

            try {
                _grContext.ResetContext();
                var renderTarget = new GRBackendRenderTarget(
                    size.Width,
                    size.Height,
                    0,
                    0,
                    new GRGlFramebufferInfo(texture.Id, 0x8058) // GL_RGBA8
                );

                var skSurface = SKSurface.Create(_grContext, renderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888);
                if (skSurface == null) {
                    Raylib.UnloadRenderTexture(texture);
                    renderTarget.Dispose();
                    throw new InvalidOperationException("Failed to create Skia surface");
                }

                Console.WriteLine("Skia surface created with test pattern");
                return new RaylibSkiaSurface(skSurface, texture, renderScaling);
            }
            catch (Exception ex) {
                Console.WriteLine($"Error creating Skia surface: {ex}");
                Raylib.UnloadRenderTexture(texture);
                throw;
            }
        }

        ISkiaSurface? ISkiaGpu.TryCreateSurface(PixelSize size, ISkiaGpuRenderSession? session)
            => session is RaylibSkiaGpuRenderSession raylibSession
                ? CreateSurface(size, raylibSession.Surface.RenderScaling)
                : null;

        public void Dispose() {
        }
    }
}