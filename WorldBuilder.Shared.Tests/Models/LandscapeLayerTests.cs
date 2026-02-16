using MemoryPack;
using Moq;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using Xunit;

namespace WorldBuilder.Shared.Tests.Models {
    public class LandscapeLayerTests {
        [Fact]
        public void Constructor_SetsCorrectProperties() {
            var id = "test_layer";
            var layer = new LandscapeLayer(id, false) { Name = "My Layer" };

            Assert.Equal(id, layer.Id);
            Assert.Equal("My Layer", layer.Name);
            Assert.False(layer.IsBase);
        }

        [Fact]
        public void IsBase_Flag_TrackingWorks() {
            var baseLayer = new LandscapeLayer("base", true);
            var normalLayer = new LandscapeLayer("normal", false);

            Assert.True(baseLayer.IsBase);
            Assert.False(normalLayer.IsBase);
        }

        [Fact]
        public void Serialization_PreservesProperties() {
            var layer = new LandscapeLayer("ser_test", true) { Name = "Serialized" };
            var serialized = MemoryPackSerializer.Serialize(layer);
            var deserialized = MemoryPackSerializer.Deserialize<LandscapeLayer>(serialized);

            Assert.NotNull(deserialized);
            Assert.Equal("ser_test", deserialized.Id);
            Assert.Equal("Serialized", deserialized.Name);
            Assert.Equal(layer.IsBase, deserialized.IsBase);
        }
        [Fact]
        public void SetVertex_HandlesBoundaries() {
            var id = "test_layer";
            var layer = new LandscapeLayer(id, false);
            var doc = new LandscapeDocument(1);

            var regionMock = new Mock<ITerrainInfo>();
            regionMock.Setup(r => r.MapWidthInVertices).Returns(1024);
            regionMock.Setup(r => r.LandblockVerticeLength).Returns(9);
            doc.Region = regionMock.Object;

            var entry = new TerrainEntry { Height = 10 };

            // Vertex 64 is on the boundary between chunk (0,0) and (1,0)
            // (64 / 64 = 1, so it belongs to chunk (1,0) naturally)
            uint vertexIndex = 64;
            layer.SetVertex(vertexIndex, doc, entry);

            // Assert it's in the primary chunk (1,0)
            var (chunkId, localIndex) = doc.GetLocalVertexIndex(vertexIndex);
            Assert.Equal(LandscapeChunk.GetId(1, 0), chunkId);
            Assert.True(layer.Chunks.ContainsKey(chunkId));
            Assert.True(layer.Chunks[chunkId].Vertices.ContainsKey(localIndex));
            Assert.Equal(entry.Height, layer.Chunks[chunkId].Vertices[localIndex].Height);

            // Assert it's also in the neighbor chunk (0,0)
            var neighborChunkId = LandscapeChunk.GetId(0, 0);
            ushort neighborLocalIndex = (ushort)(64); // (0 * 65 + 64) since y=0
            Assert.True(layer.Chunks.ContainsKey(neighborChunkId));
            Assert.True(layer.Chunks[neighborChunkId].Vertices.ContainsKey(neighborLocalIndex));
            Assert.Equal(entry.Height, layer.Chunks[neighborChunkId].Vertices[neighborLocalIndex].Height);
        }

        [Fact]
        public void TryGetVertex_ReturnsCorrectData() {
            var id = "test_layer";
            var layer = new LandscapeLayer(id, false);
            var doc = new LandscapeDocument(1);

            var regionMock = new Mock<ITerrainInfo>();
            regionMock.Setup(r => r.MapWidthInVertices).Returns(1024);
            regionMock.Setup(r => r.LandblockVerticeLength).Returns(9);
            doc.Region = regionMock.Object;

            var entry = new TerrainEntry { Height = 25 };
            layer.SetVertex(100, doc, entry);

            Assert.True(layer.TryGetVertex(100, doc, out var result));
            Assert.Equal(entry.Height, result.Height);

            Assert.False(layer.TryGetVertex(200, doc, out _));
        }
    }
}