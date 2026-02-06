using System;
using System.Collections.Generic;
using MemoryPack;
using WorldBuilder.Shared.Models;
using Xunit;

namespace WorldBuilder.Shared.Tests.Models {
    public class LandscapeLayerDocumentTests {
        #region Initialization

        [Fact]
        public void CreateId_GeneratesUniqueIds() {
            var id1 = LandscapeLayerDocument.CreateId();
            var id2 = LandscapeLayerDocument.CreateId();
            Assert.NotEqual(id1, id2);
            Assert.StartsWith("LandscapeLayerDocument_", id1);
        }

        [Fact]
        public void Constructor_WithValidId_Succeeds() {
            var id = LandscapeLayerDocument.CreateId();
            var doc = new LandscapeLayerDocument(id);
            Assert.Equal(id, doc.Id);
        }

        [Fact]
        public void Constructor_WithInvalidIdFormat_ThrowsException() {
            Assert.Throws<ArgumentException>(() => new LandscapeLayerDocument("Invalid_Id"));
        }

        #endregion

        #region Terrain Data

        [Fact]
        public void Terrain_AddEntry_StoresCorrectly() {
            var doc = new LandscapeLayerDocument(LandscapeLayerDocument.CreateId());
            var entry = new TerrainEntry(10, 1, 0, 0, 0);
            doc.Terrain[123] = entry;

            Assert.True(doc.Terrain.ContainsKey(123));
            Assert.Equal(entry.Height, doc.Terrain[123].Height);
        }

        [Fact]
        public void Terrain_UpdateEntry_OverwritesPrevious() {
            var doc = new LandscapeLayerDocument(LandscapeLayerDocument.CreateId());
            doc.Terrain[123] = new TerrainEntry(10, 1, 0, 0, 0);
            doc.Terrain[123] = new TerrainEntry(20, 2, 0, 0, 0);

            Assert.Equal((byte)20, doc.Terrain[123].Height);
            Assert.Equal((byte)2, doc.Terrain[123].Type);
        }

        [Fact]
        public void Terrain_RemoveEntry_DeletesData() {
            var doc = new LandscapeLayerDocument(LandscapeLayerDocument.CreateId());
            doc.Terrain[123] = new TerrainEntry(10, 1, 0, 0, 0);
            doc.Terrain.Remove(123);

            Assert.False(doc.Terrain.ContainsKey(123));
        }

        #endregion

        #region Serialization

        [Fact]
        public void Serialize_WithLargeTerrain_PreservesData() {
            var doc = new LandscapeLayerDocument(LandscapeLayerDocument.CreateId());
            for (uint i = 0; i < 1000; i++) {
                doc.Terrain[i] = new TerrainEntry((byte)(i % 256), 1, 0, 0, 0);
            }

            var serialized = doc.Serialize();
            var deserialized = BaseDocument.Deserialize<LandscapeLayerDocument>(serialized);
            Assert.NotNull(deserialized);

            Assert.Equal(1000, deserialized.Terrain.Count);
            Assert.Equal(doc.Terrain[500].Height, deserialized.Terrain[500].Height);
        }

        [Fact]
        public void Serialize_WithMixedEntries_PreservesFlags() {
            var doc = new LandscapeLayerDocument(LandscapeLayerDocument.CreateId());
            doc.Terrain[1] = TerrainEntry.FromHeight(10);
            doc.Terrain[2] = TerrainEntry.FromTexture(5);
            doc.Terrain[3] = new TerrainEntry(10, 5, 2, 1, 3);

            var serialized = doc.Serialize();
            var deserialized = BaseDocument.Deserialize<LandscapeLayerDocument>(serialized);
            Assert.NotNull(deserialized);

            Assert.Equal((byte)10, deserialized.Terrain[1].Height);
            Assert.Null(deserialized.Terrain[1].Type);

            Assert.Equal((byte)5, deserialized.Terrain[2].Type);
            Assert.Null(deserialized.Terrain[2].Height);

            Assert.NotNull(deserialized.Terrain[3].Height);
            Assert.NotNull(deserialized.Terrain[3].Type);
            Assert.NotNull(deserialized.Terrain[3].Scenery);
            Assert.NotNull(deserialized.Terrain[3].Road);
            Assert.NotNull(deserialized.Terrain[3].Encounters);
        }

        #endregion
    }
}
