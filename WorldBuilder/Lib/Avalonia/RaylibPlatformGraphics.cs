using System;
using System.Threading;
using Avalonia.Platform;

namespace WorldBuilder.Lib.Avalonia {
    internal sealed class RaylibPlatformGraphics : IPlatformGraphics, IDisposable {
        private RaylibSkiaGpu? _context;
        private int _refCount;

        bool IPlatformGraphics.UsesSharedContext => true;

        public RaylibSkiaGpu GetSharedContext() {
            if (Volatile.Read(ref _refCount) == 0)
                ThrowDisposed();

            if (_context is null || _context.IsLost) {
                _context?.Dispose();
                _context = null;
                _context = new RaylibSkiaGpu(RaylibPlatform.GRContext);
            }

            return _context;
        }

        private static void ThrowDisposed()
            => throw new ObjectDisposedException(nameof(RaylibPlatformGraphics));

        IPlatformGraphicsContext IPlatformGraphics.CreateContext()
            => throw new NotSupportedException();

        IPlatformGraphicsContext IPlatformGraphics.GetSharedContext()
            => GetSharedContext();

        public void AddRef() {
            Interlocked.Increment(ref _refCount);
        }

        public void Release() {
            int newRefCount = Interlocked.Decrement(ref _refCount);
            if (newRefCount == 0)
                Dispose();
        }

        public void Dispose() {
            if (_context is not null) {
                _context.Dispose();
                _context = null;
            }
        }
    }
}