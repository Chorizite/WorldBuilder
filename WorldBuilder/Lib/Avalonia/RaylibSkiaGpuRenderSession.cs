using Avalonia.Skia;
using SkiaSharp;

namespace WorldBuilder.Lib.Avalonia {
    internal sealed class RaylibSkiaGpuRenderSession : ISkiaGpuRenderSession {
        public RaylibSkiaSurface Surface { get; }
        public GRContext GrContext { get; }

        SKSurface ISkiaGpuRenderSession.SkSurface => Surface.SkSurface;
        double ISkiaGpuRenderSession.ScaleFactor => Surface.RenderScaling;
        GRSurfaceOrigin ISkiaGpuRenderSession.SurfaceOrigin => GRSurfaceOrigin.TopLeft;

        public RaylibSkiaGpuRenderSession(RaylibSkiaSurface surface, GRContext grContext) {
            Surface = surface;
            GrContext = grContext;
        }

        public void Dispose() {
            Surface.SkSurface.Flush(true);
            Surface.DrawCount++;
        }
    }
}