using Avalonia.Skia;
using SkiaSharp;
using System;

namespace WorldBuilder.Lib.Avalonia {
    internal sealed class RaylibSkiaRenderTarget : ISkiaGpuRenderTarget {
        private readonly RaylibSkiaSurface _surface;
        private readonly GRContext _grContext;
        private readonly double _renderScaling;

        public bool IsCorrupted
            => _surface.IsDisposed || _grContext.IsAbandoned || _renderScaling != _surface.RenderScaling;

        public RaylibSkiaRenderTarget(RaylibSkiaSurface surface, GRContext grContext) {
            _renderScaling = surface.RenderScaling;
            _surface = surface;
            _grContext = grContext;
        }

        public ISkiaGpuRenderSession BeginRenderingSession()
            => new RaylibSkiaGpuRenderSession(_surface, _grContext);

        public void Dispose() {
            Console.WriteLine("RaylibSkiaRenderTarget disposed");
        }
    }
}