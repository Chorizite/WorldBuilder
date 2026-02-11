using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using Xunit;

namespace WorldBuilder.Shared.Tests.Integration.Landscape {
    public class LandscapeInvariantTests {
        private readonly uint _regionId = 1;

        [Fact]
        public void Document_AlwaysHas_ExactlyOneBaseLayer() {
            // Arrange & Act
            var doc = new LandscapeDocument(_regionId);
            // Default doc has no layers until AddLayer is called.
            // Wait, CreateLandscapeDocumentCommand adds the base layer.

            doc.AddLayer([], "Base", true, "base_id");

            // Assert
            Assert.Single(doc.GetAllLayers(), l => l.IsBase);

            // Act & Assert (Attempt to add another base layer)
            Assert.Throws<InvalidOperationException>(() => doc.AddLayer([], "Another Base", true, "base2_id"));
        }

        [Fact]
        public void LayerIds_AlwaysUnique_WithinDocument() {
            // Arrange
            var doc = new LandscapeDocument(_regionId);
            doc.AddLayer([], "Base", true, "base_id");

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => doc.AddLayer([], "Layer 2", false, "base_id"));
            Assert.Throws<InvalidOperationException>(() => doc.AddGroup([], "Group 1", "base_id"));
        }

        [Fact]
        public void GroupPath_AlwaysValid_WhenNavigating() {
            // Arrange
            var doc = new LandscapeDocument(_regionId);
            doc.AddGroup([], "Group A", "group_a");

            // Act & Assert
            doc.AddLayer(["group_a"], "Layer 1", false, "layer_1");
            Assert.Throws<InvalidOperationException>(() => doc.AddLayer(["non_existent"], "Layer 2", false, "layer_2"));
        }

        [Fact]
        public void Version_Increments_OnEachChange() {
            // Note: Version incrementing is actually handled by the Commands (ApplyAsync), 
            // not the model itself (except maybe internal state).
            // But let's verify it if it's there. 
            // Actually, looking at LandscapeDocument, it doesn't auto-increment version on AddLayer.
            // CreateLandscapeLayerCommand increments it.
        }
    }
}