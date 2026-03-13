using MemoryPack;
using System.Collections.Generic;
using System.Numerics;

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
        [MemoryPackOrder(1)] public uint Flags { get; init; }

        /// <summary>
        /// CellStructure
        /// </summary>
        [MemoryPackOrder(2)] public ushort CellStructure { get; init; }

        /// <summary>
        /// Local offset position of this cell.
        /// </summary>
        [MemoryPackOrder(3)] public Vector3 Position { get; set; }

        /// <summary>
        /// Rotation quaternion.
        /// </summary>
        [MemoryPackOrder(4)] public Quaternion Rotation { get; set; } = Quaternion.Identity;

        /// <summary>
        /// Surfaces
        /// </summary>
        [MemoryPackOrder(5)] public List<ushort> Surfaces { get; init; } = [];

        /// <summary>
        /// Portals to other cells
        /// </summary>
        [MemoryPackOrder(6)] public List<WbCellPortal> Portals { get; init; } = [];

        /// <summary>
        /// Restriction Obj
        /// </summary>
        [MemoryPackOrder(7)] public uint RestrictionObj { get; init; }

        /// <summary>
        /// Visible Cells
        /// </summary>
        [MemoryPackOrder(8)] public List<ushort> VisibleCells { get; init; } = [];

        /// <summary>
        /// Objects in this Cell
        /// </summary>
        [MemoryPackOrder(9)] public Dictionary<ulong, StaticObject> StaticObjects { get; init; } = [];

        /// <summary>
        /// The Landscape Layer responsible for owning this Cell
        /// </summary>
        [MemoryPackOrder(10)] public string LayerId { get; set; } = string.Empty;
        /// <summary>
        /// The cell ID of this cell.
        /// </summary>
        [MemoryPackOrder(11)] public uint CellId { get; set; }

        /// <summary>
        /// The minimum bounds of this cell in world space.
        /// </summary>
        [MemoryPackOrder(12)] public Vector3 MinBounds { get; set; }

        /// <summary>
        /// The maximum bounds of this cell in world space.
        /// </summary>
        [MemoryPackOrder(13)] public Vector3 MaxBounds { get; set; }
    }
}
