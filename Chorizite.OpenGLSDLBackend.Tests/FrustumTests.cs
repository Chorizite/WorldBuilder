using Chorizite.Core.Lib;
using Chorizite.OpenGLSDLBackend;
using System.Numerics;
using Xunit;

namespace Chorizite.OpenGLSDLBackend.Tests {
    public class FrustumTests {
        [Fact]
        public void Frustum_ContainsBox_ReturnsTrue() {
            var frustum = new Frustum();
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