using MemoryPack;

namespace WorldBuilder.Shared.Models;

/// <summary>
/// Represents a group of landscape layers in the layer tree.
/// </summary>
[MemoryPackable]
public partial class LandscapeLayerGroup : LandscapeLayerBase {
    /// <summary>The child layers and groups.</summary>
    [MemoryPackInclude]
    [MemoryPackOrder(0)]
    public List<LandscapeLayerBase> Children { get; init; } = [];

    /// <summary>Initializes a new instance of the <see cref="LandscapeLayerGroup"/> class.</summary>
    [MemoryPackConstructor]
    public LandscapeLayerGroup() : base() { }

    /// <summary>Initializes a new instance of the <see cref="LandscapeLayerGroup"/> class with a name.</summary>
    /// <param name="name">The group name.</param>
    public LandscapeLayerGroup(string name) : base() {
        Name = name;
    }
}