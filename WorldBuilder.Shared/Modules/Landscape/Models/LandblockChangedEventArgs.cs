using System;
using System.Collections.Generic;

namespace WorldBuilder.Shared.Modules.Landscape.Models;

[Flags]
public enum LandblockChangeType {
    None = 0,
    Terrain = 1 << 0,
    Objects = 1 << 1,
    Cells = 1 << 2,
    All = Terrain | Objects | Cells
}

public class LandblockChangedEventArgs : EventArgs {
    public IEnumerable<(int x, int y)>? AffectedLandblocks { get; }
    public LandblockChangeType ChangeType { get; }

    public LandblockChangedEventArgs(IEnumerable<(int x, int y)>? affectedLandblocks, LandblockChangeType changeType = LandblockChangeType.All) {
        AffectedLandblocks = affectedLandblocks;
        ChangeType = changeType;
    }
}