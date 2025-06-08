using System;
using Avalonia.Skia;
using Raylib_cs;
using SkiaSharp;

namespace WorldBuilder.Lib.Avalonia {
    internal sealed class RaylibSkiaSurface : ISkiaSurface {
        public SKSurface SkSurface { get; }
        public RenderTexture2D Texture { get; }
        public double RenderScaling { get; set; }
        public ulong DrawCount { get; set; }
        public bool IsDisposed { get; private set; }

        SKSurface ISkiaSurface.Surface => SkSurface;
        bool ISkiaSurface.CanBlit => true;

        public RaylibSkiaSurface(SKSurface skSurface, RenderTexture2D texture, double renderScaling) {
            SkSurface = skSurface ?? throw new ArgumentNullException(nameof(skSurface));
            Texture = texture;
            RenderScaling = renderScaling;
            IsDisposed = false;
        }

        void ISkiaSurface.Blit(SKCanvas canvas) {
            if (IsDisposed || SkSurface == null)
                throw new ObjectDisposedException(nameof(RaylibSkiaSurface));
            canvas.DrawSurface(SkSurface, 0, 0);
        }

        public void Dispose() {
            if (IsDisposed)
                return;

            IsDisposed = true;
            SkSurface?.Dispose();
            Raylib.UnloadRenderTexture(Texture);
            Console.WriteLine("RaylibSkiaSurface disposed");
        }
    }
}