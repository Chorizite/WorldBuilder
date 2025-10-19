﻿using Chorizite.Core.Render.Enums;
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

        public static void FillIndex16(byte[] src, Palette palette, Span<byte> dst, int width, int height) {
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    var srcIdx = (y * width + x) * 2;
                    var palIdx = (ushort)(src[srcIdx] | (src[srcIdx + 1] << 8));
                    var color = palette.Colors[palIdx];
                    var dstIdx = (y * width + x) * 4;
                    dst[dstIdx + 0] = color.Red;
                    dst[dstIdx + 1] = color.Green;
                    dst[dstIdx + 2] = color.Blue;
                    dst[dstIdx + 3] = color.Alpha;
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