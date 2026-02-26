using System;
using System.Numerics;
using WorldBuilder.Shared.Numerics;
using Xunit;

namespace WorldBuilder.Shared.Tests.Numerics {
    public class GeometryUtilsTests {
        [Fact]
        public void RayIntersectsBox_OriginInside_ReturnsTrue() {
            var min = new Vector3(-1, -1, -1);
            var max = new Vector3(1, 1, 1);
            var origin = new Vector3(0, 0, 0);
            var direction = new Vector3(1, 0, 0);

            bool hit = GeometryUtils.RayIntersectsBox(origin, direction, min, max, out float distance);

            Assert.True(hit);
            Assert.Equal(0f, distance);
        }

        [Fact]
        public void RayIntersectsBox_OriginOutside_Intersects_ReturnsTrue() {
            var min = new Vector3(-1, -1, -1);
            var max = new Vector3(1, 1, 1);
            var origin = new Vector3(-5, 0, 0);
            var direction = new Vector3(1, 0, 0);

            bool hit = GeometryUtils.RayIntersectsBox(origin, direction, min, max, out float distance);

            Assert.True(hit);
            Assert.Equal(4f, distance);
        }

        [Fact]
        public void RayIntersectsBox_OriginOutside_Misses_ReturnsFalse() {
            var min = new Vector3(-1, -1, -1);
            var max = new Vector3(1, 1, 1);
            var origin = new Vector3(-5, 5, 0);
            var direction = new Vector3(1, 0, 0);

            bool hit = GeometryUtils.RayIntersectsBox(origin, direction, min, max, out float distance);

            Assert.False(hit);
        }

        [Fact]
        public void RayIntersectsBox_OriginOutside_OppositeDirection_ReturnsFalse() {
            var min = new Vector3(-1, -1, -1);
            var max = new Vector3(1, 1, 1);
            var origin = new Vector3(-5, 0, 0);
            var direction = new Vector3(-1, 0, 0);

            bool hit = GeometryUtils.RayIntersectsBox(origin, direction, min, max, out float distance);

            Assert.False(hit);
        }
    }
}
