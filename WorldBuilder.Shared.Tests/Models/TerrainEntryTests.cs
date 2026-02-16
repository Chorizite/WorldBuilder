using System;
using WorldBuilder.Shared.Models;
using Xunit;

namespace WorldBuilder.Shared.Tests.Models {
    public class TerrainEntryTests {

        [Fact]
        public void Pack_Unpack_RoundTrip_PreservesValues() {
            // Arrange
            var original = new TerrainEntry {
                Height = 127,
                Type = 1,
                Scenery = 2,
                Road = 3
            };

            // Act
            uint packed = original.Pack();
            var unpacked = TerrainEntry.Unpack(packed);

            // Assert
            Assert.Equal(original.Height, unpacked.Height);
            Assert.NotNull(unpacked.Type);
            Assert.Equal(original.Type, unpacked.Type);
            Assert.Equal(original.Scenery, unpacked.Scenery);
            Assert.Equal(original.Road, unpacked.Road);
            Assert.Equal(original.Flags, unpacked.Flags);
        }

        [Fact]
        public void Flags_Are_Synchronized_When_Set_Individually() {
            // Arrange
            var entry = new TerrainEntry {
                Height = 42,
                Type = 5,
                Scenery = 9,
                Road = 3
            };

            // Assert
            Assert.Equal(TerrainEntryFlags.Height | TerrainEntryFlags.Texture | TerrainEntryFlags.Scenery | TerrainEntryFlags.Road, entry.Flags);
        }

        [Fact]
        public void Flags_Are_Updated_When_Nullified() {
            // Arrange
            var entry = new TerrainEntry(100, 3, 4, 5, null);
            Assert.Equal(TerrainEntryFlags.Height | TerrainEntryFlags.Texture | TerrainEntryFlags.Scenery | TerrainEntryFlags.Road, entry.Flags);

            // Act
            entry.Height = null; // remove height

            // Assert
            Assert.False(entry.Flags.HasFlag(TerrainEntryFlags.Height));
            Assert.True(entry.Flags.HasFlag(TerrainEntryFlags.Texture));
            Assert.True(entry.Flags.HasFlag(TerrainEntryFlags.Scenery));
            Assert.True(entry.Flags.HasFlag(TerrainEntryFlags.Road));
        }

        [Fact]
        public void FromHeight_Sets_Height_Only() {
            // Act
            var entry = TerrainEntry.FromHeight(88);

            // Assert
            Assert.Equal((byte)88, entry.Height);
            Assert.Null(entry.Type);
            Assert.Null(entry.Road);
            Assert.Equal(TerrainEntryFlags.Height, entry.Flags);
        }

        [Fact]
        public void FromTextureScenery_Sets_TextureScenery_Only() {
            // Act
            var entry = TerrainEntry.FromTextureScenery(4, 7);

            // Assert
            Assert.Null(entry.Height);
            Assert.NotNull(entry.Type);
            Assert.Equal((byte)4, entry.Type);
            Assert.Equal((byte)7, entry.Scenery);
            Assert.Null(entry.Road);
            Assert.Equal(TerrainEntryFlags.Texture | TerrainEntryFlags.Scenery, entry.Flags);
        }

        [Fact]
        public void FromRoad_Sets_Road_Only() {
            // Act
            var entry = TerrainEntry.FromRoad(3);

            // Assert
            Assert.Null(entry.Height);
            Assert.Null(entry.Type);
            Assert.Equal((byte?)3, entry.Road);
            Assert.Equal(TerrainEntryFlags.Road, entry.Flags);
        }

        [Fact]
        public void Unpack_Respects_Flags() {
            // Arrange
            var entry = new TerrainEntry {
                Height = 200,
                Type = 2,
                Scenery = 3,
                Road = null
            };
            uint packed = entry.Pack();

            // Act
            var unpacked = TerrainEntry.Unpack(packed);

            // Assert
            Assert.True(unpacked.Flags.HasFlag(TerrainEntryFlags.Height));
            Assert.True(unpacked.Flags.HasFlag(TerrainEntryFlags.Texture));
            Assert.True(unpacked.Flags.HasFlag(TerrainEntryFlags.Scenery));
            Assert.False(unpacked.Flags.HasFlag(TerrainEntryFlags.Road));
            Assert.Equal((byte)200, unpacked.Height);
            Assert.Equal((byte)2, unpacked.Type);
                        Assert.Equal((byte)3, unpacked.Scenery);
                        Assert.Null(unpacked.Road);
                    }
            
                    [Fact]
                    public void Merge_OverlaysNonNullValues() {
                        // Arrange
                        var baseEntry = new TerrainEntry {
                            Height = 10,
                            Type = 1,
                            Scenery = 2,
                            Road = 3
                        };
                        var overlay = new TerrainEntry {
                            Height = 20,
                            Type = null, // Should not overwrite
                            Scenery = 5,
                            Road = null  // Should not overwrite
                        };
            
                        // Act
                        baseEntry.Merge(overlay);
            
                        // Assert
                        Assert.Equal((byte)20, baseEntry.Height);
                        Assert.Equal((byte)1, baseEntry.Type);
                        Assert.Equal((byte)5, baseEntry.Scenery);
                        Assert.Equal((byte)3, baseEntry.Road);
                        Assert.Equal(TerrainEntryFlags.Height | TerrainEntryFlags.Texture | TerrainEntryFlags.Scenery | TerrainEntryFlags.Road, baseEntry.Flags);
                    }
                }
            }
            