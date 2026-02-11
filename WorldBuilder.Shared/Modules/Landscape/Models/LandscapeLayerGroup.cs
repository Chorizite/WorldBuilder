using MemoryPack;

namespace WorldBuilder.Shared.Models;

/// <summary>
/// Represents a group of landscape layers in the layer tree.
/// </summary>
[MemoryPackable]
public partial class LandscapeLayerGroup : LandscapeLayerBase {
    /// <summary>The child layers and groups contained within this group.</summary>
    [MemoryPackInclude]
    [MemoryPackOrder(10)]
    public List<LandscapeLayerBase> Children { get; set; } = []; // List of LandscapeLayer or LandscapeLayerGroup

    /// <summary>Initializes a new instance of the <see cref="LandscapeLayerGroup"/> class.</summary>
    [MemoryPackConstructor]
    public LandscapeLayerGroup() { }

    /// <summary>Initializes a new instance of the <see cref="LandscapeLayerGroup"/> class with a specific name.</summary>
    /// <param name="name">The group name.</param>
    public LandscapeLayerGroup(string name) {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }
}