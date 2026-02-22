using MemoryPack;
using WorldBuilder.Shared.Models;
using Xunit;

namespace WorldBuilder.Shared.Tests.Models {
    public class LandscapeLayerGroupTests {
        [Fact]
        public void Constructor_InitializesEmptyChildrenList() {
            var group = new LandscapeLayerGroup("Main Group");
            Assert.NotNull(group.Children);
            Assert.Empty(group.Children);
            Assert.Equal("Main Group", group.Name);
        }

        [Fact]
        public void AddChild_AddsToChildren() {
            var group = new LandscapeLayerGroup("Parent");
            var child = new LandscapeLayer("child", false);
            group.Children.Add(child);

            Assert.Single(group.Children);
            Assert.Equal(child, group.Children[0]);
        }

        [Fact]
        public void RemoveChild_RemovesFromChildren() {
            var group = new LandscapeLayerGroup("Parent");
            var child = new LandscapeLayer("child", false);
            group.Children.Add(child);
            group.Children.Remove(child);

            Assert.Empty(group.Children);
        }

        [Fact]
        public void Serialization_PreservesHierarchy() {
            var root = new LandscapeLayerGroup("Root") { Id = "root" };
            var childGroup = new LandscapeLayerGroup("ChildGroup") { Id = "cg" };
            var layer = new LandscapeLayer("layer", false) { Name = "Layer" };

            childGroup.Children.Add(layer);
            root.Children.Add(childGroup);

            var serialized = MemoryPackSerializer.Serialize(root);
            var deserialized = MemoryPackSerializer.Deserialize<LandscapeLayerGroup>(serialized);

            Assert.NotNull(deserialized);
            Assert.Equal("root", (string)deserialized.Id);
            Assert.Single(deserialized.Children);

            var deserializedChildGroup = Assert.IsType<LandscapeLayerGroup>(deserialized.Children[0]);
            Assert.Equal("cg", deserializedChildGroup.Id);
            Assert.Single(deserializedChildGroup.Children);

            var deserializedLayer = Assert.IsType<LandscapeLayer>(deserializedChildGroup.Children[0]);
            Assert.Equal("layer", deserializedLayer.Id);
        }
    }
}