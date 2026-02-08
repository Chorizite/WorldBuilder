using MemoryPack;

namespace WorldBuilder.Shared.Models;

[MemoryPackable]
public partial class LandscapeLayerGroup : LandscapeLayerBase {
    [MemoryPackInclude]
    [MemoryPackOrder(10)]
    public List<LandscapeLayerBase> Children { get; set; } = []; // List of LandscapeLayer or LandscapeLayerGroup

    [MemoryPackConstructor]
    public LandscapeLayerGroup() { }

    public LandscapeLayerGroup(string name) {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }
}