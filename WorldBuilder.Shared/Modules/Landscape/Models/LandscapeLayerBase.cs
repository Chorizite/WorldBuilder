using MemoryPack;

namespace WorldBuilder.Shared.Models {
    /// <summary>
    /// Base class for landscape layers and layer groups.
    /// </summary>
    [MemoryPackable]
    [MemoryPackUnion(0, typeof(LandscapeLayer))]
    [MemoryPackUnion(1, typeof(LandscapeLayerGroup))]
    public abstract partial class LandscapeLayerBase {
        /// <summary>The unique identifier for the layer or group.</summary>
        [MemoryPackOrder(0)] public virtual string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>The name of the layer or group.</summary>
        [MemoryPackOrder(1)] public virtual string Name { get; set; } = string.Empty;

        /// <summary>Whether the layer or group is visible in the editor.</summary>
        [MemoryPackOrder(2)] public virtual bool IsVisible { get; set; } = true;

        /// <summary>Whether the layer or group should be included in the export.</summary>
        [MemoryPackOrder(3)] public virtual bool IsExported { get; set; } = true;

        /// <summary>The ID of the parent group, or null if it's at the root.</summary>
        [MemoryPackIgnore] public virtual string? ParentId { get; set; }
    }
}