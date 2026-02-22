using System;
using Xunit;
using WorldBuilder.Shared.Modules.Landscape.Lib;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Tests.Modules.Landscape {
    public class SplitDirTest {
        [Fact]
        public void FindSWtoNECell() {
            for (uint y = 0; y < 8; y++) {
                for (uint x = 0; x < 8; x++) {
                    var dir = TerrainUtils.CalculateSplitDirection(0, x, 0, y);
                    if (dir == CellSplitDirection.SWtoNE) {
                        return;
                    }
                }
            }
            Assert.Fail("No SWtoNE found");
        }
    }
}
