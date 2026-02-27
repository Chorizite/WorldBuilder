using System.Numerics;
using WorldBuilder.Shared.Modules.Landscape.Tools;

namespace WorldBuilder.Shared.Modules.Landscape.Models {
    /// <summary>
    /// Represents a hit result from a scene-wide raycast.
    /// </summary>
    public struct SceneRaycastHit : ISelectedObjectInfo {
        /// <summary>Whether anything was hit.</summary>
        public bool Hit;
        
        /// <summary>The type of object hit.</summary>
        public InspectorSelectionType Type { get; set; }
        
        /// <summary>The distance from the ray origin to the hit point.</summary>
        public float Distance { get; set; }
        
        /// <summary>The world position of the hit object or point.</summary>
        public Vector3 Position { get; set; }

        /// <summary>The local position of the hit object (relative to its parent landblock).</summary>
        public Vector3 LocalPosition { get; set; }
        
        /// <summary>The rotation of the hit object (if applicable).</summary>
        public Quaternion Rotation { get; set; }
        
        /// <summary>The landblock ID containing the hit.</summary>
        public uint LandblockId { get; set; }
        
        /// <summary>The instance ID of the hit object.</summary>
        public ulong InstanceId { get; set; }

        /// <summary>The secondary ID of the hit object (if applicable).</summary>
        public ushort SecondaryId { get; set; }
        
        /// <summary>The object ID (from dats) of the hit object.</summary>
        public uint ObjectId { get; set; }
        
        /// <summary>The surface normal at the hit point.</summary>
        public Vector3 Normal { get; set; }
        
        /// <summary>The X vertex coordinate (for terrain hits).</summary>
        public int VertexX { get; set; }
        
        /// <summary>The Y vertex coordinate (for terrain hits).</summary>
        public int VertexY { get; set; }

        /// <summary>A predefined 'no hit' result.</summary>
        public static SceneRaycastHit NoHit => new() { Hit = false, Distance = float.MaxValue, Type = InspectorSelectionType.None };
    }
}
