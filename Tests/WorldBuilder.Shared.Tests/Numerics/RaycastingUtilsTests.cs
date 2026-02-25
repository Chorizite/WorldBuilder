using System.Numerics;
using WorldBuilder.Shared.Numerics;
using Xunit;

namespace WorldBuilder.Shared.Tests.Numerics {
    public class RaycastingUtilsTests {
        [Fact]
        public void RayIntersectsPolygon_IntersectsConvexPolygon() {
            var vertices = new[] {
                new Vector3(0, 0, 0),
                new Vector3(10, 0, 0),
                new Vector3(10, 10, 0),
                new Vector3(0, 10, 0)
            };
            var rayOrigin = new Vector3(5, 5, 10);
            var rayDirection = new Vector3(0, 0, -1);

            bool hit = RaycastingUtils.RayIntersectsPolygon(rayOrigin, rayDirection, vertices, out float distance);

            Assert.True(hit);
            Assert.Equal(10f, distance);
        }

        [Fact]
        public void RayIntersectsPolygon_MissesPolygon() {
            var vertices = new[] {
                new Vector3(0, 0, 0),
                new Vector3(10, 0, 0),
                new Vector3(10, 10, 0),
                new Vector3(0, 10, 0)
            };
            var rayOrigin = new Vector3(15, 15, 10);
            var rayDirection = new Vector3(0, 0, -1);

            bool hit = RaycastingUtils.RayIntersectsPolygon(rayOrigin, rayDirection, vertices, out float distance);

            Assert.False(hit);
        }

        [Fact]
        public void RayIntersectsPolygon_ParallelRay_Misses() {
            var vertices = new[] {
                new Vector3(0, 0, 0),
                new Vector3(10, 0, 0),
                new Vector3(10, 10, 0),
                new Vector3(0, 10, 0)
            };
            var rayOrigin = new Vector3(5, 5, 10);
            var rayDirection = new Vector3(1, 0, 0);

            bool hit = RaycastingUtils.RayIntersectsPolygon(rayOrigin, rayDirection, vertices, out float distance);

            Assert.False(hit);
        }

        [Fact]
        public void RayIntersectsPolygon_FacingAway_Misses() {
            var vertices = new[] {
                new Vector3(0, 0, 0),
                new Vector3(10, 0, 0),
                new Vector3(10, 10, 0),
                new Vector3(0, 10, 0)
            };
            var rayOrigin = new Vector3(5, 5, 10);
            var rayDirection = new Vector3(0, 0, 1);

            bool hit = RaycastingUtils.RayIntersectsPolygon(rayOrigin, rayDirection, vertices, out float distance);

            Assert.False(hit);
        }
    }
}
