using MemoryPack;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Models {
    /// <summary>
    /// Represents a single terrain layer.
    /// </summary>
    [MemoryPackable]
    public partial class LandscapeLayer : LandscapeLayerBase {
        /// <summary>Whether this is the base layer.</summary>
        [MemoryPackOrder(0)] public bool IsBase { get; set; }

        /// <summary>Initializes a new instance of the <see cref="LandscapeLayer"/> class.</summary>
        [MemoryPackConstructor]
        public LandscapeLayer() : base() { }

        /// <summary>Initializes a new instance of the <see cref="LandscapeLayer"/> class with an ID and base status.</summary>
        public LandscapeLayer(string id, bool isBase = false) : base() {
            Id = id;
            IsBase = isBase;
        }
    }
}