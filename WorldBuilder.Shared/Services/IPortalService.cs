using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Numerics;

namespace WorldBuilder.Shared.Services {
    public interface IPortalService {
        IEnumerable<PortalData> GetPortalsForLandblock(uint regionId, uint landblockId);
        PortalData? GetPortal(uint regionId, uint landblockId, uint cellId, uint portalIndex);

        /// <summary>
        /// Returns outside-facing portals grouped by building index, along with the
        /// set of EnvCell IDs reachable from each building's entry portals.
        /// Used for per-building stencil rendering.
        /// </summary>
        IEnumerable<BuildingPortalGroup> GetPortalsByBuilding(uint regionId, uint landblockId);
    }

    public class PortalData {
        public uint CellId { get; set; }
        public uint PortalIndex { get; set; }
        public Vector3[] Vertices { get; set; } = System.Array.Empty<Vector3>();
        public BoundingBox BoundingBox { get; set; }
    }

    /// <summary>
    /// Groups outside-facing portal polygons by which building they belong to,
    /// along with the set of interior EnvCell IDs reachable from that building.
    /// </summary>
    public class BuildingPortalGroup {
        public int BuildingIndex { get; set; }
        public List<PortalData> Portals { get; set; } = new();
        public HashSet<uint> EnvCellIds { get; set; } = new();
    }
}
