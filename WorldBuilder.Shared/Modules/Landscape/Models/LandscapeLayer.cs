using MemoryPack;

namespace WorldBuilder.Shared.Models;

[MemoryPackable]
public partial class LandscapeLayer : LandscapeLayerBase {
    [MemoryPackOrder(11)] public bool IsBase { get; init; }

    public LandscapeLayer(string id, bool isBase = false) {
        Id = id;
        IsBase = isBase;
    }
}