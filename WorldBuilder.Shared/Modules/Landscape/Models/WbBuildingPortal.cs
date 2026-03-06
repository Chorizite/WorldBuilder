using MemoryPack;
using System.Collections.Generic;

namespace WorldBuilder.Shared.Modules.Landscape.Models {
    /// <summary>
    /// MemoryPackable representation of a BuildingPortal.
    /// </summary>
    [MemoryPackable]
    public partial class WbBuildingPortal {
        [MemoryPackOrder(0)] public uint Flags { get; init; }
        [MemoryPackOrder(1)] public ushort OtherCellId { get; init; }
        [MemoryPackOrder(2)] public ushort OtherPortalId { get; init; }
        [MemoryPackOrder(3)] public List<ushort> StabList { get; init; } = [];

        [MemoryPackConstructor]
        public WbBuildingPortal() { }

        public WbBuildingPortal(DatReaderWriter.Types.BuildingPortal portal) {
            Flags = (uint)portal.Flags;
            OtherCellId = portal.OtherCellId;
            OtherPortalId = portal.OtherPortalId;
            StabList = new List<ushort>(portal.StabList ?? []);
        }

        public DatReaderWriter.Types.BuildingPortal ToDatPortal() {
            return new DatReaderWriter.Types.BuildingPortal {
                Flags = (DatReaderWriter.Enums.PortalFlags)Flags,
                OtherCellId = OtherCellId,
                OtherPortalId = OtherPortalId,
                StabList = new List<ushort>(StabList ?? [])
            };
        }
    }
}
