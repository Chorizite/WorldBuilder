using MemoryPack;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Modules.Landscape.Tools;

namespace WorldBuilder.Shared.Modules.Landscape.Models {
    /// <summary>
    /// Represents a hit result from a scene-wide raycast.
    /// </summary>
    public struct SceneRaycastHit : ISelectedObjectInfo {
        /// <summary>Whether anything was hit.</summary>
        public bool Hit;
        
        /// <summary>The type of object hit.</summary>
        public ObjectType Type { get; set; }
        
        /// <summary>The distance from the ray origin to the hit point.</summary>
        public float Distance { get; set; }
        
        /// <summary>The world position of the hit object or point.</summary>
        public Vector3 Position { get; set; }

        /// <summary>The local position of the hit object (relative to its parent landblock).</summary>
        public Vector3 LocalPosition { get; set; }
        
        /// <summary>The rotation of the hit object (if applicable).</summary>
        public Quaternion Rotation { get; set; }

        public float X {
            get => LocalPosition.X;
            set => LocalPosition = new Vector3(value, LocalPosition.Y, LocalPosition.Z);
        }
        public float Y {
            get => LocalPosition.Y;
            set => LocalPosition = new Vector3(LocalPosition.X, value, LocalPosition.Z);
        }
        public float Z {
            get => LocalPosition.Z;
            set => LocalPosition = new Vector3(LocalPosition.X, LocalPosition.Y, value);
        }

        public float RotationX {
            get => GeometryUtils.QuaternionToEuler(Rotation).X;
            set => Rotation = GeometryUtils.EulerToQuaternion(new Vector3(value, RotationY, RotationZ));
        }
        public float RotationY {
            get => GeometryUtils.QuaternionToEuler(Rotation).Y;
            set => Rotation = GeometryUtils.EulerToQuaternion(new Vector3(RotationX, value, RotationZ));
        }
        public float RotationZ {
            get => GeometryUtils.QuaternionToEuler(Rotation).Z;
            set => Rotation = GeometryUtils.EulerToQuaternion(new Vector3(RotationX, RotationY, value));
        }
        
        /// <summary>The landblock ID containing the hit.</summary>
        public ushort LandblockId { get; set; }

        /// <summary>The environment cell ID containing the hit (if applicable).</summary>
        public uint? CellId { get; set; }
        
        /// <summary>The layer ID containing the hit.</summary>
        public string LayerId { get; set; }

        /// <summary>The instance ID of the hit object.</summary>
        public ObjectId InstanceId { get; set; }

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

        public SceneryDisqualificationReason DisqualificationReason { get; set; }

        /// <summary>A predefined 'no hit' result.</summary>
        public static SceneRaycastHit NoHit => new() { Hit = false, Distance = float.MaxValue, Type = ObjectType.None };
    }
}
