using MemoryPack;

namespace WorldBuilder.Shared.Models;

/// <summary>
/// Represents a single terrain layer within a landscape document.
/// </summary>
[MemoryPackable]
public partial class LandscapeLayer : LandscapeLayerBase {
    /// <summary>Whether this layer is the base layer (immutable representation of the .dat data).</summary>
    [MemoryPackOrder(11)] public bool IsBase { get; init; }

    /// <summary>Initializes a new instance of the <see cref="LandscapeLayer"/> class.</summary>
    /// <param name="id">The unique identifier for the layer.</param>
    /// <param name="isBase">Whether this is the base layer.</param>
    public LandscapeLayer(string id, bool isBase = false) {
        Id = id;
        IsBase = isBase;
    }

    /// <summary>The terrain data stored in this layer, mapping vertex index to terrain entry.</summary>
    [MemoryPackInclude]
    [MemoryPackOrder(12)]
    public Dictionary<uint, TerrainEntry> Terrain { get; init; } = [];
}