using MemoryPack;

namespace WorldBuilder.Shared.Models {
    /// <summary>
    /// Base class for items in the landscape layer tree (layers and groups).
    /// </summary>
    [MemoryPackable]
    [MemoryPackUnion(0, typeof(LandscapeLayer))]
    [MemoryPackUnion(1, typeof(LandscapeLayerGroup))]
    public abstract partial class LandscapeLayerBase {
        /// <summary>The unique identifier for the layer or group.</summary>
        [MemoryPackInclude]
        [MemoryPackOrder(0)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>The display name of the layer or group.</summary>
        [MemoryPackInclude]
        [MemoryPackOrder(1)]
        public string Name { get; set; } = "New Layer";

        /// <summary>Whether this item should be included in the export.</summary>
        [MemoryPackInclude]
        [MemoryPackOrder(2)]
        public bool IsExported { get; set; } = true;

        /// <summary>Whether this item is visible in the editor.</summary>
        [MemoryPackIgnore]
        public bool IsVisible { get; set; } = true;
    }
}