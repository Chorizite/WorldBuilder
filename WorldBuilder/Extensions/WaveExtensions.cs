using DatReaderWriter.DBObjs;

namespace WorldBuilder.Extensions {
    /// <summary>
    /// Extension methods for Wave objects
    /// </summary>
    public static class WaveExtensions {
        /// <summary>
        /// Represents a WAV file header parsed from raw bytes
        /// </summary>
        public class WavHeader {
            public short Format { get; }
            public short Channels { get; }
            public int SampleRate { get; }
            public int ByteRate { get; }
            public short BlockAlign { get; }
            public short BitsPerSample { get; }

            public WavHeader(byte[] headerBytes) {
                if (headerBytes.Length < 16) {
                    throw new ArgumentException("Header must be at least 16 bytes", nameof(headerBytes));
                }

                Format = BitConverter.ToInt16(headerBytes, 0);
                Channels = BitConverter.ToInt16(headerBytes, 2);
                SampleRate = BitConverter.ToInt32(headerBytes, 4);
                ByteRate = BitConverter.ToInt32(headerBytes, 8);
                BlockAlign = BitConverter.ToInt16(headerBytes, 12);
                BitsPerSample = BitConverter.ToInt16(headerBytes, 14);
            }

            /// <summary>
            /// Calculates the duration of a wave file based on data size and this header
            /// </summary>
            public float GetDuration(int dataBytes) {
                var bytesPerSecond = SampleRate * Channels * (BitsPerSample / 8);
                return (float)dataBytes / bytesPerSecond;
            }
        }

        /// <summary>
        /// Parses the raw 16-byte header field from a Wave object
        /// </summary>
        public static WavHeader ParseHeader(this Wave wave) {
            return new WavHeader(wave.Header);
        }

        /// <summary>
        /// Checks if this Wave object is actually an MP3 file
        /// </summary>
        public static bool IsMp3(this Wave wave) {
            return wave.Header.Length > 0 && wave.Header[0] == 0x55;
        }

        /// <summary>
        /// Converts a Wave object to a playable WAV stream
        /// </summary>
        public static Stream ToWavStream(this Wave wave) {
            var stream = new MemoryStream();
           
            using (var binaryWriter = new BinaryWriter(stream, System.Text.Encoding.ASCII, true)) {
                binaryWriter.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));

                uint filesize = (uint)(wave.Data.Length + 36); // 36 is added for all the extra we're adding for the WAV header format
                binaryWriter.Write(filesize);

                binaryWriter.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

                binaryWriter.Write(System.Text.Encoding.ASCII.GetBytes("fmt"));
                binaryWriter.Write((byte)0x20); // Null ending to the fmt

                binaryWriter.Write(0x10); // 16 ... length of all the above

                // AC audio headers start at Format Type,
                // and are usually 18 bytes, with some exceptions
                // notably objectID A000393 which is 30 bytes

                // WAV headers are always 16 bytes from Format Type to end of header,
                // so this extra data is truncated here.
                binaryWriter.Write(wave.Header.Take(16).ToArray());


                binaryWriter.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                binaryWriter.Write((uint)wave.Data.Length);
                binaryWriter.Write(wave.Data);
            }
            
            stream.Position = 0; // Reset stream position for playback
            return stream;
        }
    }
}
