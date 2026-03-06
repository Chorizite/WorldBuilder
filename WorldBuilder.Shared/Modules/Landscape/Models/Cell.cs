using DatReaderWriter.Types;
using MemoryPack;

namespace WorldBuilder.Shared.Modules.Landscape.Models {
    /// <summary>
    /// EnvCell.
    /// </summary>
    [MemoryPackable]
    public partial class Cell {
        /// <summary>
        /// Environment file id
        /// </summary>
        [MemoryPackOrder(0)] public ushort EnvironmentId { get; init; }

        /// <summary>
        /// Cell flags (e.g. SeenOutside)
        /// </summary>
        [MemoryPackOrder(7)] public uint Flags { get; init; }

        /// <summary>
        /// CellStructure
        /// </summary>
        [MemoryPackOrder(1)] public ushort CellStructure { get; init; }

        /// <summary>
        /// Local offset position of this cell.
        /// </summary>
        [MemoryPackOrder(2)] public float[] Position { get; init; } = [];

        /// <summary>
        /// Surfaces
        /// </summary>
        [MemoryPackIgnore] public List<ushort> Surfaces { get; init; } = [];

        /// <summary>
        /// Portals to other cells
        /// </summary>
        [MemoryPackIgnore] public List<CellPortal> Portals { get; init; } = [];

        /// <summary>
        /// Objects in this Cell
        /// </summary>
        [MemoryPackOrder(5)] public Dictionary<ulong, StaticObject> StaticObjects { get; init; } = [];

        /// <summary>
        /// The Landscape Layer responsible for owning this Cell
        /// </summary>
        [MemoryPackOrder(6)] public string LayerId { get; set; } = string.Empty;
    }
}
