using MemoryPack;
using Moq;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using Xunit;
using System.Linq;

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
    }
}