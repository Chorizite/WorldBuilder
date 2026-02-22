using System;
using System.Collections.Generic;
using System.Linq;
using WorldBuilder.Shared.Models;
using Xunit;

namespace WorldBuilder.Shared.Tests.Modules.Landscape {
    public class LandscapeDocumentHierarchicalTests {
        [Fact]
        public void IsItemVisible_ShouldRespectHierarchy() {
            var doc = new LandscapeDocument(1);
            var group = new LandscapeLayerGroup("Group") { Id = "group1", IsVisible = false };
            var layer = new LandscapeLayer("layer1") { IsVisible = true };

            group.Children.Add(layer);
            doc.LayerTree.Add(group);

            Assert.False(doc.IsItemVisible(group));
            Assert.False(doc.IsItemVisible(layer));

            group.IsVisible = true;
            Assert.True(doc.IsItemVisible(group));
            Assert.True(doc.IsItemVisible(layer));

            layer.IsVisible = false;
            Assert.False(doc.IsItemVisible(layer));
        }

        [Fact]
        public void IsItemExported_ShouldRespectHierarchy() {
            var doc = new LandscapeDocument(1);
            var group = new LandscapeLayerGroup("Group") { Id = "group1", IsExported = false };
            var layer = new LandscapeLayer("layer1") { IsExported = true };

            group.Children.Add(layer);
            doc.LayerTree.Add(group);

            Assert.False(doc.IsItemExported(group));
            Assert.False(doc.IsItemExported(layer));

            group.IsExported = true;
            Assert.True(doc.IsItemExported(group));
            Assert.True(doc.IsItemExported(layer));

            layer.IsExported = false;
            Assert.False(doc.IsItemExported(layer));
        }

        [Fact]
        public void IsItemVisible_DeepHierarchy() {
            var doc = new LandscapeDocument(1);
            var root = new LandscapeLayerGroup("Root") { Id = "root", IsVisible = true };
            var mid = new LandscapeLayerGroup("Mid") { Id = "mid", IsVisible = false };
            var leaf = new LandscapeLayer("Leaf") { Id = "leaf", IsVisible = true };

            root.Children.Add(mid);
            mid.Children.Add(leaf);
            doc.LayerTree.Add(root);

            Assert.True(doc.IsItemVisible(root));
            Assert.False(doc.IsItemVisible(mid));
            Assert.False(doc.IsItemVisible(leaf));

            mid.IsVisible = true;
            Assert.True(doc.IsItemVisible(leaf));

            root.IsVisible = false;
            Assert.False(doc.IsItemVisible(leaf));
        }
    }
}