using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Numerics;

namespace WorldBuilder.Shared.Services {
    public interface IPortalService {
        IEnumerable<PortalData> GetPortalsForLandblock(uint regionId, uint landblockId);
        PortalData? GetPortal(uint regionId, uint landblockId, uint cellId, uint portalIndex);
    }

    public class PortalData {
        public uint CellId { get; set; }
        public uint PortalIndex { get; set; }
        public Vector3[] Vertices { get; set; } = System.Array.Empty<Vector3>();
        public BoundingBox BoundingBox { get; set; }
    }
}
