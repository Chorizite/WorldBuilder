using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using WorldBuilder.Modules.DatBrowser.ViewModels;
using WorldBuilder.Shared.Services;
using Xunit;

namespace WorldBuilder.Tests.Modules.DatBrowser {
    public class ReflectionNodeViewModelTests {
        [Fact]
        public void Create_WithByteArray_ReturnsNodeWithNoChildrenAndCorrectValue() {
            // Arrange
            var mockDats = new Mock<IDatReaderWriter>();
            var bytes = new byte[] { 1, 2, 3, 4, 5 };
            var name = "TestBytes";

            // Act
            var node = ReflectionNodeViewModel.Create(name, bytes, mockDats.Object);

            // Assert
            Assert.Equal(name, node.Name);
            Assert.Equal("byte[]", node.Value);
            Assert.Equal("5 bytes", node.TypeName);
            Assert.Null(node.Children);
        }

        [Fact]
        public void Create_WithOtherObject_ReturnsNodeWithChildren() {
            // Arrange
            var mockDats = new Mock<IDatReaderWriter>();
            var obj = new { Prop1 = 1, Prop2 = "two" };
            var name = "TestObj";

            // Act
            var node = ReflectionNodeViewModel.Create(name, obj, mockDats.Object);

            // Assert
            Assert.Equal(name, node.Name);
            Assert.Null(node.Value);
            Assert.NotNull(node.Children);
            Assert.Equal(2, node.Children.Count);
        }
    }
}
