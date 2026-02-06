using MemoryPack;
using System;

namespace WorldBuilder.Shared.Models {
    /// <summary>
    /// Flags that indicate which fields are present in TerrainEntry
    /// </summary>
    [Flags]
    public enum TerrainEntryFlags : byte {
        None = 0,
        Height = 1 << 0,
        Texture = 1 << 1,
        Scenery = 1 << 2,
        Road = 1 << 3,
        Encounters = 1 << 4,
    }

    /// <summary>
    /// Represents a terrain entry with optional height, texture, scenery, road, encounters, and flags.
    /// Flags are automatically synchronized with nullable fields.
    /// Packed format: [Height:8][Texture:5][Scenery:5][Encounters:4][Road:3][Unused:2][Flags:5]
    /// </summary>
    [MemoryPackable]
    public partial struct TerrainEntry {
        private const int HEIGHT_SHIFT = 24;
        private const uint HEIGHT_MASK = 0xFF000000;

        private const int TEXTURE_SHIFT = 19;
        private const uint TEXTURE_MASK = 0x00F80000;

        private const int SCENERY_SHIFT = 14;
        private const uint SCENERY_MASK = 0x0007C000;

        private const int ENCOUNTERS_SHIFT = 10;
        private const uint ENCOUNTERS_MASK = 0x00003C00;

        private const int ROAD_SHIFT = 7;
        private const uint ROAD_MASK = 0x00000380;

        private const int FLAGS_SHIFT = 0;
        private const uint FLAGS_MASK = 0x0000001F;

        [MemoryPackInclude]
        [MemoryPackOrder(0)]
        private uint _data;

        [MemoryPackIgnore]
        public TerrainEntryFlags Flags {
            get => (TerrainEntryFlags)((_data & FLAGS_MASK) >> FLAGS_SHIFT);
            private set => _data = (_data & ~FLAGS_MASK) | ((uint)value << FLAGS_SHIFT);
        }

        [MemoryPackIgnore]
        public byte? Height {
            get => Flags.HasFlag(TerrainEntryFlags.Height)
                ? (byte)((_data & HEIGHT_MASK) >> HEIGHT_SHIFT)
                : null;
            set {
                if (value.HasValue) {
                    _data = (_data & ~HEIGHT_MASK) | ((uint)value.Value << HEIGHT_SHIFT);
                    Flags |= TerrainEntryFlags.Height;
                }
                else {
                    _data &= ~HEIGHT_MASK;
                    Flags &= ~TerrainEntryFlags.Height;
                }
            }
        }

        [MemoryPackIgnore]
        public byte? Type {
            get => Flags.HasFlag(TerrainEntryFlags.Texture)
                ? (byte)((_data & TEXTURE_MASK) >> TEXTURE_SHIFT)
                : null;
            set {
                if (value.HasValue) {
                    if (value.Value > 31)
                        throw new ArgumentOutOfRangeException(nameof(Type), "Texture must be 0-31");
                    _data = (_data & ~TEXTURE_MASK) | ((uint)value.Value << TEXTURE_SHIFT);
                    Flags |= TerrainEntryFlags.Texture;
                }
                else {
                    _data &= ~TEXTURE_MASK;
                    Flags &= ~TerrainEntryFlags.Texture;
                }
            }
        }

        [MemoryPackIgnore]
        public byte? Scenery {
            get => Flags.HasFlag(TerrainEntryFlags.Scenery)
                ? (byte)((_data & SCENERY_MASK) >> SCENERY_SHIFT)
                : null;
            set {
                if (value.HasValue) {
                    if (value.Value > 31)
                        throw new ArgumentOutOfRangeException(nameof(Scenery), "Scenery must be 0-31");
                    _data = (_data & ~SCENERY_MASK) | ((uint)value.Value << SCENERY_SHIFT);
                    Flags |= TerrainEntryFlags.Scenery;
                }
                else {
                    _data &= ~SCENERY_MASK;
                    Flags &= ~TerrainEntryFlags.Scenery;
                }
            }
        }

        [MemoryPackIgnore]
        public byte? Road {
            get => Flags.HasFlag(TerrainEntryFlags.Road)
                ? (byte)((_data & ROAD_MASK) >> ROAD_SHIFT)
                : null;
            set {
                if (value.HasValue) {
                    if (value.Value > 7)
                        throw new ArgumentOutOfRangeException(nameof(Road), "Road must be 0-7");
                    _data = (_data & ~ROAD_MASK) | ((uint)value.Value << ROAD_SHIFT);
                    Flags |= TerrainEntryFlags.Road;
                }
                else {
                    _data &= ~ROAD_MASK;
                    Flags &= ~TerrainEntryFlags.Road;
                }
            }
        }

        [MemoryPackIgnore]
        public byte? Encounters {
            get => Flags.HasFlag(TerrainEntryFlags.Encounters)
                ? (byte)((_data & ENCOUNTERS_MASK) >> ENCOUNTERS_SHIFT)
                : null;
            set {
                if (value.HasValue) {
                    if (value.Value > 15)
                        throw new ArgumentOutOfRangeException(nameof(Encounters), "Encounters must be 0-15");
                    _data = (_data & ~ENCOUNTERS_MASK) | ((uint)value.Value << ENCOUNTERS_SHIFT);
                    Flags |= TerrainEntryFlags.Encounters;
                }
                else {
                    _data &= ~ENCOUNTERS_MASK;
                    Flags &= ~TerrainEntryFlags.Encounters;
                }
            }
        }

        public TerrainEntry() {
            _data = 0;
        }

        public TerrainEntry(byte? height, byte? texture, byte? scenery, byte? road, byte? encounters) {
            _data = 0;

            if (texture.HasValue && texture.Value > 31)
                throw new ArgumentOutOfRangeException(nameof(texture), "Texture must be 0-31");
            if (scenery.HasValue && scenery.Value > 31)
                throw new ArgumentOutOfRangeException(nameof(scenery), "Scenery must be 0-31");
            if (road.HasValue && road.Value > 7)
                throw new ArgumentOutOfRangeException(nameof(road), "Road must be 0-7");
            if (encounters.HasValue && encounters.Value > 15)
                throw new ArgumentOutOfRangeException(nameof(encounters), "Encounters must be 0-15");

            Height = height;
            Type = texture;
            Scenery = scenery;
            Road = road;
            Encounters = encounters;
        }

        public static TerrainEntry FromHeight(byte height) => new(height, null, null, null, null);

        public static TerrainEntry FromTexture(byte texture) => new(null, texture, null, null, null);

        public static TerrainEntry FromScenery(byte scenery) => new(null, null, scenery, null, null);

        public static TerrainEntry FromRoad(byte road) => new(null, null, null, road, null);

        public static TerrainEntry FromEncounters(byte encounters) => new(null, null, null, null, encounters);

        public static TerrainEntry FromTextureScenery(byte texture, byte scenery) => new(null, texture, scenery, null, null);

        /// <summary>
        /// Returns the packed 32-bit representation
        /// </summary>
        public uint Pack() => _data;

        /// <summary>
        /// Creates a TerrainEntry from a packed 32-bit value
        /// </summary>
        public static TerrainEntry Unpack(uint packed) {
            return new TerrainEntry { _data = packed };
        }

        /// <summary>
        /// Merges another entry into this one. Null values are ignored.
        /// </summary>
        public void Merge(TerrainEntry value) {
            if (value.Height.HasValue) Height = value.Height;
            if (value.Type.HasValue) Type = value.Type;
            if (value.Scenery.HasValue) Scenery = value.Scenery;
            if (value.Road.HasValue) Road = value.Road;
            if (value.Encounters.HasValue) Encounters = value.Encounters;
        }

        public override string ToString() {
            string height = Height?.ToString() ?? "null";
            string texture = Type?.ToString() ?? "null";
            string scenery = Scenery?.ToString() ?? "null";
            string road = Road?.ToString() ?? "null";
            string encounters = Encounters?.ToString() ?? "null";

            return $"Height:{height}, Texture:{texture}, Scenery:{scenery}, Road:{road}, Encounters:{encounters}, Flags:{Flags}";
        }
    }
}