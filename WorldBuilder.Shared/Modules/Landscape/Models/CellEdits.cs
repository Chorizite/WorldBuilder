using MemoryPack;
using System.Collections.Generic;

namespace WorldBuilder.Shared.Modules.Landscape.Models
{
    /// <summary>
    /// Tracks modifications applied globally to the Base DAT objects within a specific Environment Cell.
    /// Exists on the LandscapeDocument to represent base-cell edits.
    /// </summary>
    [MemoryPackable]
    public partial class CellEdits
    {
        /// <summary>
        /// Base interior static objects that have been modified from their native dat positions.
        /// Mapped by their pseudo-ID (array index in original dat).
        /// </summary>
        [MemoryPackOrder(0)] public Dictionary<uint, StaticObject> ModifiedBaseObjects { get; init; } = [];

        /// <summary>
        /// Pseudo-IDs of base interior static objects that have been deleted or moved to a different layer.
        /// </summary>
        [MemoryPackOrder(1)] public List<uint> RemovedBaseObjectIds { get; init; } = [];

        /// <summary>
        /// Global topology overrides for the cell (Portals, Surfaces, etc).
        /// If this is non-null, the standard dat-cell geometry definitions are ignored.
        /// </summary>
        [MemoryPackOrder(2)] public Cell? StructureOverrides { get; set; }
    }
}
