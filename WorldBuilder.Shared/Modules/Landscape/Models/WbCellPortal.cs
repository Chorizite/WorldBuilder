using MemoryPack;

namespace WorldBuilder.Shared.Modules.Landscape.Models {
    /// <summary>
    /// MemoryPackable representation of a CellPortal.
    /// </summary>
    [MemoryPackable]
    public partial class WbCellPortal {
        [MemoryPackOrder(0)] public uint Flags { get; init; }
        [MemoryPackOrder(1)] public ushort PolygonId { get; init; }
        [MemoryPackOrder(2)] public ushort OtherCellId { get; init; }
        [MemoryPackOrder(3)] public ushort OtherPortalId { get; init; }

        [MemoryPackConstructor]
        public WbCellPortal() { }

        public WbCellPortal(DatReaderWriter.Types.CellPortal portal) {
            Flags = (uint)portal.Flags;
            PolygonId = portal.PolygonId;
            OtherCellId = portal.OtherCellId;
            OtherPortalId = portal.OtherPortalId;
        }

        public DatReaderWriter.Types.CellPortal ToDatPortal() {
            return new DatReaderWriter.Types.CellPortal {
                Flags = (DatReaderWriter.Enums.PortalFlags)Flags,
                PolygonId = PolygonId,
                OtherCellId = OtherCellId,
                OtherPortalId = OtherPortalId
            };
        }
    }
}
