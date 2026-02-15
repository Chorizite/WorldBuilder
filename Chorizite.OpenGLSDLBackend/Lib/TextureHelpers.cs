using Chorizite.Core.Render.Enums;
using DatReaderWriter.DBObjs;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chorizite.OpenGLSDLBackend.Lib {
    public static class TextureHelpers {
        public static byte[] CreateSolidColorTexture(DatReaderWriter.Types.ColorARGB color, int width, int height) {
            var bytes = new byte[width * height * 4];
            for (int i = 0; i < width * height; i++) {
                bytes[i * 4 + 0] = color.Red;
                bytes[i * 4 + 1] = color.Green;
                bytes[i * 4 + 2] = color.Blue;
                bytes[i * 4 + 3] = color.Alpha;
            }
            return bytes;
        }

        public static void FillIndex16(byte[] src, Palette palette, Span<byte> dst, int width, int height, bool isClipMap = false) {
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    var srcIdx = (y * width + x) * 2;
                    var palIdx = (ushort)(src[srcIdx] | (src[srcIdx + 1] << 8));
                    var color = palette.Colors[palIdx];
                    var dstIdx = (y * width + x) * 4;

                    if (isClipMap && palIdx < 8) {
                        dst[dstIdx + 0] = 0;
                        dst[dstIdx + 1] = 0;
                        dst[dstIdx + 2] = 0;
                        dst[dstIdx + 3] = 0;
                    }
                    else {
                        dst[dstIdx + 0] = color.Red;
                        dst[dstIdx + 1] = color.Green;
                        dst[dstIdx + 2] = color.Blue;
                        dst[dstIdx + 3] = color.Alpha;
                    }
                }
            }
        }

        public static void FillP8(byte[] src, Palette palette, Span<byte> dst, int width, int height, bool isClipMap = false) {
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    var srcIdx = (y * width + x);
                    var palIdx = src[srcIdx];
                    var color = palette.Colors[palIdx];
                    var dstIdx = (y * width + x) * 4;

                    if (isClipMap && palIdx < 8) {
                        dst[dstIdx + 0] = 0;
                        dst[dstIdx + 1] = 0;
                        dst[dstIdx + 2] = 0;
                        dst[dstIdx + 3] = 0;
                    }
                    else {
                        dst[dstIdx + 0] = color.Red;
                        dst[dstIdx + 1] = color.Green;
                        dst[dstIdx + 2] = color.Blue;
                        dst[dstIdx + 3] = color.Alpha;
                    }
                }
            }
        }

        public static void FillR5G6B5(byte[] src, Span<byte> dst, int width, int height) {
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    var srcIdx = (y * width + x) * 2;
                    var val = (ushort)(src[srcIdx] | (src[srcIdx + 1] << 8));
                    var dstIdx = (y * width + x) * 4;

                    dst[dstIdx + 0] = (byte)(((val & 0xF800) >> 11) << 3);
                    dst[dstIdx + 1] = (byte)(((val & 0x7E0) >> 5) << 2);
                    dst[dstIdx + 2] = (byte)((val & 0x1F) << 3);
                    dst[dstIdx + 3] = 255;
                }
            }
        }

        public static void FillA4R4G4B4(byte[] src, Span<byte> dst, int width, int height) {
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    var srcIdx = (y * width + x) * 2;
                    var val = (ushort)(src[srcIdx] | (src[srcIdx + 1] << 8));
                    var dstIdx = (y * width + x) * 4;

                    dst[dstIdx + 0] = (byte)(((val >> 8) & 0x0F) * 17);
                    dst[dstIdx + 1] = (byte)(((val >> 4) & 0x0F) * 17);
                    dst[dstIdx + 2] = (byte)((val & 0x0F) * 17);
                    var alpha = (byte)(((val >> 12) & 0x0F) * 17);
                    dst[dstIdx + 3] = alpha == 0 ? (byte)255 : alpha;
                }
            }
        }

        /// <summary>
        /// Checks if a pixel format is compressed
        /// </summary>
        public static bool IsCompressedFormat(DatReaderWriter.Enums.PixelFormat format) {
            return format == DatReaderWriter.Enums.PixelFormat.PFID_DXT1 ||
                   format == DatReaderWriter.Enums.PixelFormat.PFID_DXT3 ||
                   format == DatReaderWriter.Enums.PixelFormat.PFID_DXT5;
        }

        /// <summary>
        /// Gets the expected compressed data size for a texture
        /// </summary>
        public static int GetCompressedLayerSize(int width, int height, TextureFormat format) {
            int blocksWide = Math.Max(1, (width + 3) / 4);
            int blocksHigh = Math.Max(1, (height + 3) / 4);
            int blockSize = format == TextureFormat.DXT1 ? 8 : 16;
            return blocksWide * blocksHigh * blockSize;
        }

        public static byte[] Color565ToRgba(ushort color565) {
            int r = (color565 >> 11) & 31;
            int g = (color565 >> 5) & 63;
            int b = color565 & 31;
            return new byte[] { (byte)(r * 255 / 31), (byte)(g * 255 / 63), (byte)(b * 255 / 31), 255 };
        }
    }
}