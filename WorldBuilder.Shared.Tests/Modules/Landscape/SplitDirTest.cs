using System;
using Xunit;

namespace WorldBuilder.Shared.Tests.Modules.Landscape
{
    public class SplitDirTest
    {
        private enum CellSplitDirection
        {
            SWtoNE,
            SEtoNW
        }

        private static CellSplitDirection CalculateSplitDirection(uint landblockX, uint cellX, uint landblockY, uint cellY)
        {
            uint seedA = (landblockX * 8 + cellX) * 214614067u;
            uint seedB = (landblockY * 8 + cellY) * 1109124029u;
            uint magicA = seedA + 1813693831u;
            uint magicB = seedB;
            float splitDir = magicA - magicB - 1369149221u;

            return splitDir * 2.3283064e-10f >= 0.5f ? CellSplitDirection.SEtoNW : CellSplitDirection.SWtoNE;
        }

        [Fact]
        public void FindSWtoNECell()
        {
            for (uint y = 0; y < 8; y++)
            {
                for (uint x = 0; x < 8; x++)
                {
                    var dir = CalculateSplitDirection(0, x, 0, y);
                    if (dir == CellSplitDirection.SWtoNE)
                    {
                        // Found one. Let's print it (via exception or ensure it matches logic)
                        // Assert.True(true, $"Found SWtoNE at {x},{y}");
                        return;
                    }
                }
            }
            Assert.Fail("No SWtoNE found");
        }
    }
}
