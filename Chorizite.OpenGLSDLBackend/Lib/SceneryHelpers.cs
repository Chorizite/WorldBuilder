using DatReaderWriter.Types;
using System;
using System.Numerics;

namespace Chorizite.OpenGLSDLBackend.Lib {
    /// <summary>
    /// Stateless utility methods for deterministic scenery placement.
    /// </summary>
    public static class SceneryHelpers {
        /// <summary>
        /// Displaces a scenery object into a pseudo-randomized location
        /// </summary>
        public static Vector3 Displace(ObjectDesc obj, uint ix, uint iy, uint iq) {
            float x;
            float y;
            float z = obj.BaseLoc.Origin.Z;
            var loc = obj.BaseLoc;

            if (obj.DisplaceX <= 0)
                x = loc.Origin.X;
            else
                x = (float)(unchecked((uint)(1813693831u * iy - (iq + 45773u) * (1360117743u * iy * ix + 1888038839u) - 1109124029u * ix))
                    * 2.3283064e-10 * obj.DisplaceX + loc.Origin.X);

            if (obj.DisplaceY <= 0)
                y = loc.Origin.Y;
            else
                y = (float)(unchecked((uint)(1813693831u * iy - (iq + 72719u) * (1360117743u * iy * ix + 1888038839u) - 1109124029u * ix))
                    * 2.3283064e-10 * obj.DisplaceY + loc.Origin.Y);

            var quadrant = unchecked((uint)(1813693831u * iy - ix * (1870387557u * iy + 1109124029u) - 402451965u)) * 2.3283064e-10f;

            if (quadrant >= 0.75) return new Vector3(y, -x, z);
            if (quadrant >= 0.5) return new Vector3(-x, -y, z);
            if (quadrant >= 0.25) return new Vector3(-y, x, z);
            return new Vector3(x, y, z);
        }

        /// <summary>
        /// Returns the scale for a scenery object
        /// </summary>
        public static float ScaleObj(ObjectDesc obj, uint x, uint y, uint k) {
            var minScale = obj.MinScale;
            var maxScale = obj.MaxScale;

            if (minScale == maxScale)
                return maxScale;

            return (float)(Math.Pow(maxScale / minScale,
                unchecked((uint)(1813693831u * y - (k + 32593u) * (1360117743u * y * x + 1888038839u) - 1109124029u * x)) * 2.3283064e-10) * minScale);
        }

        /// <summary>
        /// Returns the rotation for a scenery object as a Quaternion.
        /// </summary>
        public static Quaternion RotateObj(ObjectDesc obj, uint x, uint y, uint k, Vector3 loc) {
            var baseOrientation = new Quaternion(obj.BaseLoc.Orientation.X, obj.BaseLoc.Orientation.Y, obj.BaseLoc.Orientation.Z, obj.BaseLoc.Orientation.W);

            if (obj.MaxRotation <= 0.0f)
                return baseOrientation;

            var degrees = (float)(unchecked((uint)(1813693831u * y - (k + 63127u) * (1360117743u * y * x + 1888038839u) - 1109124029u * x))
                * 2.3283064e-10 * obj.MaxRotation);

            return SetHeading(baseOrientation, degrees);
        }

        /// <summary>
        /// Aligns an object to a plane normal, returning Quaternion.
        /// </summary>
        public static Quaternion ObjAlign(ObjectDesc obj, Vector3 normal, float z, Vector3 loc) {
            var baseOrientation = new Quaternion(obj.BaseLoc.Orientation.X, obj.BaseLoc.Orientation.Y, obj.BaseLoc.Orientation.Z, obj.BaseLoc.Orientation.W);

            var negNormal = -normal;
            var headingDeg = (450.0f - (MathF.Atan2(negNormal.Y, negNormal.X) * 180f / MathF.PI)) % 360f;

            return SetHeading(baseOrientation, headingDeg);
        }

        public static Quaternion SetHeading(Quaternion orientation, float degrees) {
            var rads = degrees * MathF.PI / 180f;

            var matrix = Matrix4x4.CreateFromQuaternion(orientation);
            var heading = new Vector3(MathF.Sin(rads), MathF.Cos(rads), matrix.M23 + matrix.M13);

            var normal = Vector3.Normalize(heading);

            // Avoid attempting to rotate if normal is too small
            if (normal.LengthSquared() < 0.0001f)
                return orientation;

            var headingDeg = MathF.Atan2(normal.Y, normal.X) * 180f / MathF.PI;
            var zDeg = 450.0f - headingDeg;
            var zRot = -(zDeg % 360.0f) * MathF.PI / 180f;

            var xRot = MathF.Asin(normal.Z);
            return Quaternion.CreateFromYawPitchRoll(xRot, 0, zRot);
        }

        /// <summary>
        /// Returns true if floor slope is within bounds for this object
        /// </summary>
        public static bool CheckSlope(ObjectDesc obj, float zNormal) {
            return zNormal >= obj.MinSlope && zNormal <= obj.MaxSlope;
        }
    }
}
