using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using System.Runtime.CompilerServices;

namespace Chorizite.OpenGLSDLBackend {
    public static class GLHelpers {
        private static ILogger? Logger;

        private static OpenGLGraphicsDevice? Device;

        public static void Init(OpenGLGraphicsDevice device, ILogger logger) {
            Logger = logger;
            Device = device;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckErrors(bool logErrors = false) {
            var error = Device?.GL.GetError();
            if (error.HasValue && error.Value != GLEnum.NoError) {
                Console.WriteLine($"OPENGL ERROR");
                Logger?.LogError($"OpenGL Error: {error}");
                throw new Exception($"OpenGL Error: {error}");
            }
        }
    }
}