using MemoryPack;

namespace WorldBuilder.Shared.Models {
    [MemoryPackable]
    [MemoryPackUnion(0, typeof(LandscapeLayer))]
    [MemoryPackUnion(1, typeof(LandscapeLayerGroup))]
    public abstract partial class LandscapeLayerBase {
        [MemoryPackInclude]
        [MemoryPackOrder(0)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [MemoryPackInclude]
        [MemoryPackOrder(1)]
        public string Name { get; set; } = "New Layer";
    }
}