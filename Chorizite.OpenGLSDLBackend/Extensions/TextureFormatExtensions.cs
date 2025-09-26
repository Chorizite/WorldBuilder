using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chorizite.OpenGLSDLBackend.Extensions {
    internal static class TextureFormatExtensions {
        public static SizedInternalFormat ToGL(this Core.Render.Enums.TextureFormat format) {
            return format switch {
                Core.Render.Enums.TextureFormat.RGBA8 => SizedInternalFormat.Rgba8,
                _ => throw new NotSupportedException($"Texture format {format} is not supported."),
            };
        }

        public static PixelFormat ToPixelFormat(this Core.Render.Enums.TextureFormat format) {
            return format switch {
                Core.Render.Enums.TextureFormat.RGBA8 => PixelFormat.Rgba,
                _ => throw new NotSupportedException($"Texture format {format} is not supported."),
            };
        }
    }
}
