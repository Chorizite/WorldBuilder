using Chorizite.Core.Lib;
using Chorizite.OpenGLSDLBackend;
using System.Numerics;
using Xunit;

namespace Chorizite.OpenGLSDLBackend.Tests {
    public class FrustumTests {
        [Fact]
        public void Frustum_ContainsBox_ReturnsTrue() {
            var frustum = new Frustum();
            // Identity matrix means the frustum is basically the unit cube in clip space (-1 to 1)
            // But wait, the plane extraction from identity matrix:
            // Left: M14+M11 = 0+1 = 1, M24+M21 = 0+0 = 0, M34+M31 = 0+0 = 0, M44+M41 = 1+0 = 1. -> x + 1 = 0 -> x = -1
            // Right: M14-M11 = 0-1 = -1, M24-M21 = 0, M34-M31 = 0, M44-M41 = 1-0 = 1. -> -x + 1 = 0 -> x = 1
            // So yes, it defines the unit cube.
            frustum.Update(Matrix4x4.Identity);

            var box = new BoundingBox(new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f));
            Assert.True(frustum.Intersects(box));
        }

        [Fact]
        public void Frustum_OutsideBox_ReturnsFalse() {
            var frustum = new Frustum();
            frustum.Update(Matrix4x4.Identity);

            var box = new BoundingBox(new Vector3(2f, 2f, 2f), new Vector3(3f, 3f, 3f));
            Assert.False(frustum.Intersects(box));
        }

        [Fact]
        public void Frustum_PartialOverlap_ReturnsTrue() {
            var frustum = new Frustum();
            frustum.Update(Matrix4x4.Identity);

            var box = new BoundingBox(new Vector3(0.5f, 0.5f, 0.5f), new Vector3(1.5f, 1.5f, 1.5f));
            Assert.True(frustum.Intersects(box));
        }
    }
}