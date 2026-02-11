using System;
using System.Numerics;

namespace WorldBuilder.Shared.Modules.Landscape.Models {
    /// <summary>
    /// Represents the result of a terrain raycast.
    /// </summary>
    public struct TerrainRaycastHit {
        /// <summary>Whether the ray hit the terrain.</summary>
        public bool Hit;
        /// <summary>The world position of the hit.</summary>
        public Vector3 HitPosition;
        /// <summary>The distance from the ray origin to the hit point.</summary>
        public float Distance;
        /// <summary>The ID of the hit landcell.</summary>
        public uint LandcellId;

        /// <summary>The ID of the landblock containing the hit.</summary>
        public ushort LandblockId => (ushort)(LandcellId >> 16);
        /// <summary>The X coordinate of the landblock containing the hit.</summary>
        public uint LandblockX => (uint)(LandblockId >> 8);
        /// <summary>The Y coordinate of the landblock containing the hit.</summary>
        public uint LandblockY => (uint)(LandblockId & 0xFF);

        /// <summary>The X coordinate of the cell within the landblock containing the hit.</summary>
        public uint CellX => (uint)Math.Round((HitPosition.X - (LandblockX * 192f)) / 24f, MidpointRounding.AwayFromZero);
        /// <summary>The Y coordinate of the cell within the landblock containing the hit.</summary>
        public uint CellY => (uint)Math.Round((HitPosition.Y - (LandblockY * 192f)) / 24f, MidpointRounding.AwayFromZero);

        /// <summary>Gets the world position of the nearest vertex to the hit point.</summary>
        public Vector3 NearestVertice {
            get {
                var vx = VerticeX;
                var vy = VerticeY;
                var x = (LandblockId >> 8) * 192 + vx * 24f;
                var y = (LandblockId & 0xFF) * 192 + vy * 24f;
                return new Vector3(x, y, HitPosition.Z);
            }
        }

        /// <summary>The X index of the nearest vertex to the hit point.</summary>
        public int VerticeX => (int)Math.Round((HitPosition.X - (LandblockX * 192f)) / 24f, MidpointRounding.AwayFromZero);
        /// <summary>The Y index of the nearest vertex to the hit point.</summary>
        public int VerticeY => (int)Math.Round((HitPosition.Y - (LandblockY * 192f)) / 24f, MidpointRounding.AwayFromZero);
    }
}