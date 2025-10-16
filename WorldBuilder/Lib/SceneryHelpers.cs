using DatReaderWriter.Types;
using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Lib {
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
                x = (float)((1813693831 * iy - (iq + 45773) * (1360117743 * iy * ix + 1888038839) - 1109124029 * ix)
                    * 2.3283064e-10 * obj.DisplaceX + loc.Origin.X);

            if (obj.DisplaceY <= 0)
                y = loc.Origin.Y;
            else
                y = (float)((1813693831 * iy - (iq + 72719) * (1360117743 * iy * ix + 1888038839) - 1109124029 * ix)
                    * 2.3283064e-10 * obj.DisplaceY + loc.Origin.Y);

            var quadrant = (1813693831 * iy - ix * (1870387557 * iy + 1109124029) - 402451965) * 2.3283064e-10f;

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
                (1813693831 * y - (k + 32593) * (1360117743 * y * x + 1888038839) - 1109124029 * x) * 2.3283064e-10) * minScale);
        }

        /// <summary>
        /// Returns the rotation for a scenery object as a Quaternion
        /// Z-up coordinate system: rotation around Z-axis (heading)
        /// </summary>
        public static Quaternion RotateObj(ObjectDesc obj, uint x, uint y, uint k, Vector3 loc) {
            if (obj.MaxRotation <= 0.0f)
                return Quaternion.Identity;

            var degrees = (float)((1813693831 * y - (k + 63127) * (1360117743 * y * x + 1888038839) - 1109124029 * x)
                * 2.3283064e-10 * obj.MaxRotation);
            var radians = degrees * MathF.PI / 180f;

            // Z-up: heading rotation is around Z-axis
            return Quaternion.CreateFromAxisAngle(Vector3.UnitZ, radians);
        }

        /// <summary>
        /// Aligns an object to a plane normal, returning Quaternion
        /// Z-up coordinate system: aligns object's Z-axis with terrain normal
        /// </summary>
        public static Quaternion ObjAlign(ObjectDesc obj, Vector3 normal, float z, Vector3 loc) {
            // In Z-up: we want to rotate from Vector3.UnitZ to the terrain normal
            var up = Vector3.UnitZ;

            // If normal is already pointing up, no rotation needed
            if (Vector3.Dot(normal, up) > 0.9999f)
                return Quaternion.Identity;

            // If normal is pointing down, rotate 180 degrees
            if (Vector3.Dot(normal, up) < -0.9999f)
                return Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);

            // Calculate rotation axis (perpendicular to both up and normal)
            var axis = Vector3.Cross(up, normal);
            axis = Vector3.Normalize(axis);

            // Calculate rotation angle
            var angle = MathF.Acos(Vector3.Dot(up, normal));

            return Quaternion.CreateFromAxisAngle(axis, angle);
        }

        /// <summary>
        /// Returns TRUE if floor slope is within bounds for this object
        /// </summary>
        public static bool CheckSlope(ObjectDesc obj, float zNormal) {
            return zNormal >= obj.MinSlope && zNormal <= obj.MaxSlope;
        }
    }
}