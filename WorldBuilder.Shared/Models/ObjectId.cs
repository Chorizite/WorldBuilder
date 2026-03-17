using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using MemoryPack;

namespace WorldBuilder.Shared.Models {
    /// <summary>
    /// A centralized 128-bit identifier for objects in WorldBuilder.
    /// Supports both base DAT objects and newly inserted DB objects.
    /// Serializes to string for SQLite storage: "dat:type:context:index" or "db:type:hexid".
    /// </summary>
    [MemoryPackable]
    public partial struct ObjectId : IEquatable<ObjectId> {
        [MemoryPackOrder(0)] public ulong Low => _low;
        [MemoryPackOrder(1)] public ulong High => _high;

        private readonly ulong _low;
        private readonly ulong _high;

        private const ulong DbFlag = 1UL << 63;
        private const int TypeShift = 47;
        private const ulong TypeMask = 0xFFFFUL;
        private const int StateShift = 39;
        private const ulong StateMask = 0xFFUL;

        public bool IsEmpty => _low == 0 && _high == 0;
        public bool IsDb => (_high & DbFlag) != 0;
        public bool IsDat => !IsDb && !IsEmpty;

        /// <summary>Whether this is a ghost ID used for dragging objects from the asset panel.</summary>
        public bool IsGhost => IsDat && Context == 0xFFFFFFFF && Index == 0xFFFF;

        public ObjectType Type => (ObjectType)((_high >> TypeShift) & TypeMask);
        public byte State => (byte)((_high >> StateShift) & StateMask);
        public uint Context => (uint)(_low >> 32);
        public ushort Index => (ushort)_low;

        /// <summary>
        /// Reconstructs a 32-bit identifier from Context and Index.
        /// For DAT objects, this correctly reconstructs the 32-bit cell ID or object index.
        /// </summary>
        public uint DataId => Type switch {
            ObjectType.EnvCell => (Context << 16) | Index,
            ObjectType.EnvCellStaticObject => Context,
            _ => 0
        };

        private ObjectId(ulong low, ulong high) {
            _low = low;
            _high = high;
        }

        public static ObjectId Empty => default;

        public static ObjectId FromDat(ObjectType type, byte state, uint context, ushort index) {
            ulong high = ((ulong)type << TypeShift) | ((ulong)state << StateShift);
            ulong low = ((ulong)context << 32) | (ulong)index;
            return new ObjectId(low, high);
        }

        public static ObjectId NewDb(ObjectType type, uint context = 0) {
            Span<byte> bytes = stackalloc byte[16];
            Random.Shared.NextBytes(bytes);
            ulong low = BitConverter.ToUInt64(bytes[..8]);
            ulong high = BitConverter.ToUInt64(bytes[8..]);
            
            // Set DB flag and Type
            high &= ~(DbFlag | (TypeMask << TypeShift)); // Clear
            high |= DbFlag | ((ulong)type << TypeShift);

            // Encode Context into upper 32 bits of low
            low &= 0xFFFFFFFFUL; // Clear upper 32 bits
            low |= ((ulong)context << 32);
            
            return new ObjectId(low, high);
        }

        public static ObjectId FromLegacyDbId(ObjectType type, ulong oldId) {
            byte state = (byte)((oldId >> 48) & 0xFF);
            uint context = (uint)((oldId >> 16) & 0xFFFFFFFF);
            ushort index = (ushort)(oldId & 0xFFFF);
            
            ulong high = DbFlag | ((ulong)type << TypeShift) | ((ulong)state << StateShift);
            ulong low = ((ulong)context << 32) | (ulong)index;
            return new ObjectId(low, high);
        }

        /// <summary>
        /// Parses an ObjectId from its string representation.
        /// </summary>
        public static ObjectId Parse(string s) {
            if (string.IsNullOrEmpty(s)) throw new ArgumentException("ID cannot be empty", nameof(s));
            if (s == "empty") return Empty;
            
            var parts = s.Split(':');
            if (parts.Length < 2) throw new FormatException($"Invalid ObjectId format: {s}");

            if (parts[0] == "dat") {
                if (parts.Length != 5) throw new FormatException($"Invalid DAT ObjectId format: {s}");
                var type = (ObjectType)Enum.Parse(typeof(ObjectType), parts[1]);
                var context = uint.Parse(parts[2], System.Globalization.NumberStyles.HexNumber);
                var index = ushort.Parse(parts[3], System.Globalization.NumberStyles.HexNumber);
                var state = byte.Parse(parts[4]);
                return FromDat(type, state, context, index);
            } else if (parts[0] == "db") {
                if (parts.Length != 3) throw new FormatException($"Invalid DB ObjectId format: {s}");
                var type = (ObjectType)Enum.Parse(typeof(ObjectType), parts[1]);
                
                ulong low, high;
                if (parts[2].Length == 32) {
                    high = ulong.Parse(parts[2][..16], System.Globalization.NumberStyles.HexNumber);
                    low = ulong.Parse(parts[2][16..], System.Globalization.NumberStyles.HexNumber);
                } else if (parts[2].Length == 16) {
                    // Legacy 64-bit DB ID
                    low = ulong.Parse(parts[2], System.Globalization.NumberStyles.HexNumber);
                    high = 0;
                } else {
                    throw new FormatException($"Invalid DB hex ID length: {parts[2]}");
                }

                // Force DB flag and Type
                high &= ~(DbFlag | (TypeMask << TypeShift)); // Clear existing type and flag
                high |= DbFlag | ((ulong)type << TypeShift); // Set new type and flag
                
                return new ObjectId(low, high);
            }

            throw new FormatException($"Unknown ObjectId prefix: {parts[0]}");
        }

        /// <summary>
        /// Tries to parse an ObjectId from its string representation.
        /// </summary>
        public static bool TryParse(string? s, [NotNullWhen(true)] out ObjectId result) {
            result = default;
            if (string.IsNullOrEmpty(s)) return false;

            try {
                result = Parse(s);
                return true;
            } catch {
                return false;
            }
        }

        public static ObjectId FromDb(string id) {
            if (string.IsNullOrEmpty(id)) return Empty;

            var parts = id.Split(':');
            if (parts.Length < 3) return Empty;

            if (parts[0] == "dat") {
                if (Enum.TryParse<ObjectType>(parts[1], out var type) &&
                    uint.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, null, out var context) &&
                    ushort.TryParse(parts[3], System.Globalization.NumberStyles.HexNumber, null, out var index)) {
                    byte state = parts.Length > 4 ? byte.Parse(parts[4]) : (byte)0;
                    return FromDat(type, state, context, index);
                }
            }
            else if (parts[0] == "db") {
                if (Enum.TryParse<ObjectType>(parts[1], out var type) &&
                    parts[2].Length == 32 &&
                    TryFullHexParse(parts[2], out var low, out var high)) {
                    // Force flag and type from part 1
                    high &= ~(DbFlag | (TypeMask << TypeShift));
                    high |= DbFlag | ((ulong)type << TypeShift);
                    return new ObjectId(low, high);
                }
            }

            return Empty;
        }

        private static bool TryFullHexParse(string hex, out ulong low, out ulong high) {
            low = 0;
            high = 0;
            try {
                high = ulong.Parse(hex[..16], System.Globalization.NumberStyles.HexNumber);
                low = ulong.Parse(hex[16..], System.Globalization.NumberStyles.HexNumber);
                return true;
            }
            catch {
                return false;
            }
        }

        public override string ToString() {
            if (IsEmpty) return "empty";
            if (IsDat) {
                return $"dat:{Type}:{Context:X8}:{Index:X4}:{State}";
            }
            else {
                return $"db:{Type}:{_high:X16}{_low:X16}";
            }
        }

        public bool Equals(ObjectId other) => _low == other._low && _high == other._high;
        public override bool Equals([NotNullWhen(true)] object? obj) => obj is ObjectId other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_low, _high);

        public static bool operator ==(ObjectId left, ObjectId right) => left.Equals(right);
        public static bool operator !=(ObjectId left, ObjectId right) => !left.Equals(right);

        public static implicit operator string(ObjectId id) => id.ToString();
    }
}
