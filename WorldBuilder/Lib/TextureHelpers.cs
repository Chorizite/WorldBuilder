using DatReaderWriter.DBObjs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace WorldBuilder.Lib {
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

        public static void GetTexture(RenderSurface surface, Span<byte> span, Shared.Lib.IDatReaderWriter dats) {
            switch (surface.Format) {
                case DatReaderWriter.Enums.PixelFormat.PFID_INDEX16:
                    if (!dats.TryGet<Palette>(surface.DefaultPaletteId, out var paletteData))
                        throw new Exception($"Unable to load Palette: 0x{surface.DefaultPaletteId:X8}");
                    for (int y = 0; y < surface.Height; y++) {
                        for (int x = 0; x < surface.Width; x++) {
                            var srcIdx = (y * surface.Width + x) * 2;
                            var palIdx = (ushort)(surface.SourceData[srcIdx] | (surface.SourceData[srcIdx + 1] << 8));
                            var color = paletteData.Colors[palIdx];
                            var dstIdx = (y * surface.Width + x) * 4;
                            span[dstIdx + 0] = color.Red;
                            span[dstIdx + 1] = color.Green;
                            span[dstIdx + 2] = color.Blue;
                            span[dstIdx + 3] = color.Alpha;
                        }
                    }
                    break;
                case DatReaderWriter.Enums.PixelFormat.PFID_A8R8G8B8:
                    for (int x = 0; x < surface.Width; x++) {
                        for (int y = 0; y < surface.Height; y++) {
                            var idx = x + y * surface.Width;
                            span[idx * 4 + 0] = surface.SourceData[idx * 4 + 2];
                            span[idx * 4 + 1] = surface.SourceData[idx * 4 + 1];
                            span[idx * 4 + 2] = surface.SourceData[idx * 4 + 0];
                            span[idx * 4 + 3] = surface.SourceData[idx * 4 + 3];
                        }
                    }
                    break;
                case DatReaderWriter.Enums.PixelFormat.PFID_DXT1:
                    DecompressDxt1(surface.SourceData, surface.Width, surface.Height, span);
                    break;
                case DatReaderWriter.Enums.PixelFormat.PFID_DXT5:
                    DecompressDxt5(surface.SourceData, surface.Width, surface.Height, span);
                    break;
                case DatReaderWriter.Enums.PixelFormat.PFID_R8G8B8:
                    for (int x = 0; x < surface.Width; x++) {
                        for (int y = 0; y < surface.Height; y++) {
                            var idx = x + y * surface.Width;
                            span[idx * 4 + 0] = surface.SourceData[idx * 3 + 2];
                            span[idx * 4 + 1] = surface.SourceData[idx * 3 + 1];
                            span[idx * 4 + 2] = surface.SourceData[idx * 3 + 0];
                            span[idx * 4 + 3] = 255;
                        }
                    }
                    break;
                default:
                    throw new NotSupportedException($"Unsupported surface format: {surface.Format}");
            }
        }

        public static void DecompressDxt1(byte[] src, int width, int height, Span<byte> dst) {
            int srcOffset = 0;
            for (int y = 0; y < height; y += 4) {
                for (int x = 0; x < width; x += 4) {
                    ushort color0 = (ushort)(src[srcOffset] | (src[srcOffset + 1] << 8)); srcOffset += 2;
                    ushort color1 = (ushort)(src[srcOffset] | (src[srcOffset + 1] << 8)); srcOffset += 2;
                    uint codes = (uint)(src[srcOffset] | (src[srcOffset + 1] << 8) | (src[srcOffset + 2] << 16) | (src[srcOffset + 3] << 24)); srcOffset += 4;

                    var c0 = Color565ToRgba(color0);
                    var c1 = Color565ToRgba(color1);
                    var c2 = new byte[4];
                    var c3 = new byte[4];

                    bool transparent = color0 <= color1;
                    if (!transparent) {
                        for (int i = 0; i < 3; i++) {
                            c2[i] = (byte)((2 * c0[i] + c1[i]) / 3);
                            c3[i] = (byte)((c0[i] + 2 * c1[i]) / 3);
                        }
                        c2[3] = c3[3] = 255;
                    }
                    else {
                        for (int i = 0; i < 3; i++) {
                            c2[i] = (byte)((c0[i] + c1[i]) / 2);
                            c3[i] = 0;
                        }
                        c2[3] = 255;
                        c3[3] = 0;
                    }
                    c0[3] = c1[3] = 255;

                    for (int py = 0; py < 4; py++) {
                        for (int px = 0; px < 4; px++) {
                            if (x + px >= width || y + py >= height) continue;
                            int code = (int)((codes >> ((py * 4 + px) * 2)) & 3);
                            var color = code switch { 0 => c0, 1 => c1, 2 => c2, 3 => c3, _ => c0 };
                            int dstIdx = ((y + py) * width + (x + px)) * 4;
                            color.CopyTo(dst[dstIdx..(dstIdx + 4)]);
                        }
                    }
                }
            }
        }

        public static void DecompressDxt5(byte[] src, int width, int height, Span<byte> dst) {
            int srcOffset = 0;
            for (int y = 0; y < height; y += 4) {
                for (int x = 0; x < width; x += 4) {
                    byte a0 = src[srcOffset++];
                    byte a1 = src[srcOffset++];
                    ulong alphaCodes = 0;
                    for (int i = 0; i < 6; i++) alphaCodes |= ((ulong)src[srcOffset++] << (i * 8));

                    ushort color0 = (ushort)(src[srcOffset] | (src[srcOffset + 1] << 8)); srcOffset += 2;
                    ushort color1 = (ushort)(src[srcOffset] | (src[srcOffset + 1] << 8)); srcOffset += 2;
                    uint colorCodes = (uint)(src[srcOffset] | (src[srcOffset + 1] << 8) | (src[srcOffset + 2] << 16) | (src[srcOffset + 3] << 24)); srcOffset += 4;

                    var c0 = Color565ToRgba(color0);
                    var c1 = Color565ToRgba(color1);
                    var c2 = new byte[4];
                    var c3 = new byte[4];
                    for (int i = 0; i < 3; i++) {
                        c2[i] = (byte)((2 * c0[i] + c1[i]) / 3);
                        c3[i] = (byte)((c0[i] + 2 * c1[i]) / 3);
                    }
                    c0[3] = c1[3] = c2[3] = c3[3] = 0;

                    byte[] alphas = new byte[8];
                    alphas[0] = a0;
                    alphas[1] = a1;
                    bool alpha6 = a0 > a1;
                    if (alpha6) {
                        for (int i = 2; i < 8; i++) alphas[i] = (byte)(((8 - i) * a0 + (i - 1) * a1) / 7);
                    }
                    else {
                        for (int i = 2; i < 6; i++) alphas[i] = (byte)(((6 - i) * a0 + (i - 1) * a1) / 5);
                        alphas[6] = 0;
                        alphas[7] = 255;
                    }

                    for (int py = 0; py < 4; py++) {
                        for (int px = 0; px < 4; px++) {
                            if (x + px >= width || y + py >= height) continue;
                            int alphaIdx = py * 4 + px;
                            int alphaCode = (int)((alphaCodes >> (alphaIdx * 3)) & 7);
                            byte alpha = alphas[alphaCode];
                            int colorCode = (int)((colorCodes >> (alphaIdx * 2)) & 3);
                            var color = colorCode switch { 0 => c0, 1 => c1, 2 => c2, 3 => c3, _ => c0 };
                            color[3] = alpha;
                            int dstIdx = ((y + py) * width + (x + px)) * 4;
                            color.CopyTo(dst[dstIdx..(dstIdx + 4)]);
                        }
                    }
                }
            }
        }

        public static byte[] Color565ToRgba(ushort color565) {
            int r = (color565 >> 11) & 31;
            int g = (color565 >> 5) & 63;
            int b = color565 & 31;
            return new byte[] { (byte)(r * 255 / 31), (byte)(g * 255 / 63), (byte)(b * 255 / 31), 255 };
        }
    }
}
