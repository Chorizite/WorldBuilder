using System;
using System.Collections.Generic;

namespace WorldBuilder.Shared.Modules.Landscape.Models;

public class LandblockChangedEventArgs : EventArgs {
    public IEnumerable<(int x, int y)>? AffectedLandblocks { get; }

    public LandblockChangedEventArgs(IEnumerable<(int x, int y)>? affectedLandblocks) {
        AffectedLandblocks = affectedLandblocks;
    }
}