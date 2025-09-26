using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorldBuilder.Shared.Lib.Extensions {
    public static class ColorARGBExtensions {
        public static uint ToUInt32(this ColorARGB c) {
            return (uint)c.Blue << 24 | (uint)c.Green << 16 | (uint)c.Red << 8 | (uint)c.Alpha;
        }

        public static bool IsDefined(this ColorARGB c) {
            return c.Red > 0 || c.Green > 0 || c.Blue > 0 || c.Alpha > 0;
        }

        public static ColorARGB FromUInt32(uint value) {
            return new ColorARGB() {
                Blue = (byte)(value >> 24),
                Green = (byte)(value >> 16),
                Red = (byte)(value >> 8),
                Alpha = (byte)value
            };
        }
    }
}
