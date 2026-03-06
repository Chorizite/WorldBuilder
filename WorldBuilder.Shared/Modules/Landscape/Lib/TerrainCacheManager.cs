using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Lib {
    /// <summary>
    /// Manages the binary terrain cache for a region.
    /// Format: [Magic:4][Version:4][RegionId:4][Width:4][Height:4][Data:Width*Height*4]
    /// </summary>
    public static class TerrainCacheManager {
        private const uint MAGIC = 0x43544257; // "WTBC" in little-endian (WBTC)
        private const uint VERSION = 1;
        private const int HEADER_SIZE = 20;

        /// <summary>
        /// Gets the path to the terrain cache for a specific region within a project.
        /// </summary>
        public static string GetCachePath(string projectDirectory, uint regionId) {
            return Path.Combine(projectDirectory, "cache", $"terrain_{regionId}.wbtc");
        }

        /// <summary>
        /// Saves terrain data to a binary cache file.
        /// </summary>
        public static async Task SaveAsync(string path, uint[] data, uint regionId, int width, int height) {
            var directory = Path.GetDirectoryName(path);
            if (directory != null && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

            await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
            using var writer = new BinaryWriter(stream);

            writer.Write(MAGIC);
            writer.Write(VERSION);
            writer.Write(regionId);
            writer.Write(width);
            writer.Write(height);

            // Write the data array as raw bytes
            byte[] byteData = MemoryMarshal.AsBytes(data.AsSpan()).ToArray();
            await stream.WriteAsync(byteData, 0, byteData.Length);
        }

        /// <summary>
        /// Loads terrain data from a binary cache file.
        /// </summary>
        public static async Task<uint[]?> LoadAsync(string path, uint expectedRegionId, int expectedWidth, int expectedHeight) {
            if (!File.Exists(path)) return null;

            try {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
                using var reader = new BinaryWriter(stream); // We only need the stream for reading raw bytes later

                byte[] header = new byte[HEADER_SIZE];
                int read = await stream.ReadAsync(header, 0, HEADER_SIZE);
                if (read < HEADER_SIZE) return null;

                var magic = BitConverter.ToUInt32(header, 0);
                var version = BitConverter.ToUInt32(header, 4);
                var regionId = BitConverter.ToUInt32(header, 8);
                var width = BitConverter.ToInt32(header, 12);
                var height = BitConverter.ToInt32(header, 16);

                if (magic != MAGIC || version != VERSION || regionId != expectedRegionId || width != expectedWidth || height != expectedHeight) {
                    return null;
                }

                int dataSize = width * height;
                uint[] data = new uint[dataSize];
                byte[] byteData = new byte[dataSize * 4];
                
                int totalRead = 0;
                while (totalRead < byteData.Length) {
                    int r = await stream.ReadAsync(byteData, totalRead, byteData.Length - totalRead);
                    if (r == 0) break;
                    totalRead += r;
                }

                if (totalRead < byteData.Length) return null;

                Buffer.BlockCopy(byteData, 0, data, 0, byteData.Length);
                return data;
            }
            catch {
                return null;
            }
        }
    }
}
