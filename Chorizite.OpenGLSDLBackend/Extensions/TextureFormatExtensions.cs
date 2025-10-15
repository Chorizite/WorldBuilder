using Chorizite.Core.Render.Enums;
using Silk.NET.OpenGL;
using System;

namespace Chorizite.OpenGLSDLBackend.Extensions {
    internal static class TextureFormatExtensions {
        public static SizedInternalFormat ToGL(this Core.Render.Enums.TextureFormat format) {
            return format switch {
                TextureFormat.RGBA8 => SizedInternalFormat.Rgba8,
                TextureFormat.A8 => SizedInternalFormat.R8,
                TextureFormat.Rgba32f => SizedInternalFormat.Rgba32f,
                TextureFormat.DXT1 => SizedInternalFormat.CompressedRgbaS3TCDxt1Ext,
                TextureFormat.DXT3 => SizedInternalFormat.CompressedRgbaS3TCDxt3Ext,
                TextureFormat.DXT5 => SizedInternalFormat.CompressedRgbaS3TCDxt5Ext,
                _ => throw new NotSupportedException($"Texture format {format} is not supported."),
            };
        }

        public static InternalFormat ToCompressedGL(this Core.Render.Enums.TextureFormat format) {
            return format switch {
                TextureFormat.DXT1 => InternalFormat.CompressedRgbaS3TCDxt1Ext,
                TextureFormat.DXT3 => InternalFormat.CompressedRgbaS3TCDxt3Ext,
                TextureFormat.DXT5 => InternalFormat.CompressedRgbaS3TCDxt5Ext,
                _ => throw new NotSupportedException($"Texture format {format} does not support compression."),
            };
        }

        public static PixelFormat ToPixelFormat(this Core.Render.Enums.TextureFormat format) {
            return format switch {
                Core.Render.Enums.TextureFormat.RGBA8 => PixelFormat.Rgba,
                Core.Render.Enums.TextureFormat.A8 => PixelFormat.Red,
                Core.Render.Enums.TextureFormat.Rgba32f => PixelFormat.Rgba,
                _ => throw new NotSupportedException($"Texture format {format} is not supported."),
            };
        }

        public static PixelType ToPixelType(this Core.Render.Enums.TextureFormat format) {
            return format switch {
                TextureFormat.RGBA8 => PixelType.UnsignedByte,
                TextureFormat.A8 => PixelType.UnsignedByte,
                TextureFormat.Rgba32f => PixelType.Float,
                _ => throw new NotSupportedException($"Texture format {format} is not supported."),
            };
        }

        public static bool IsCompressed(this Core.Render.Enums.TextureFormat format) {
            return format == Core.Render.Enums.TextureFormat.DXT1 ||
                   format == Core.Render.Enums.TextureFormat.DXT3 ||
                   format == Core.Render.Enums.TextureFormat.DXT5;
        }
    }
}