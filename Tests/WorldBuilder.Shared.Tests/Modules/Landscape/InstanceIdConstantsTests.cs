using Xunit;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Tests.Modules.Landscape {
    public class InstanceIdConstantsTests {
        [Theory]
        [InlineData(0, InspectorSelectionType.Vertex, InstanceIdConstants.VertexFlag)]
        [InlineData(12345, InspectorSelectionType.Building, InstanceIdConstants.BuildingFlag | 12345UL)]
        [InlineData(0xFFFFFFFF, InspectorSelectionType.StaticObject, InstanceIdConstants.StaticObjectFlag | 0xFFFFFFFFUL)]
        [InlineData(1, InspectorSelectionType.Scenery, InstanceIdConstants.SceneryFlag | 1UL)]
        [InlineData(42, InspectorSelectionType.Portal, InstanceIdConstants.PortalFlag | 42UL)]
        [InlineData(100, InspectorSelectionType.None, 100UL)]
        public void Encode_CorrectlyCombinesFlagAndId(uint id, InspectorSelectionType type, ulong expected) {
            var result = InstanceIdConstants.Encode(id, type);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(InstanceIdConstants.VertexFlag | 1UL, InspectorSelectionType.Vertex)]
        [InlineData(InstanceIdConstants.BuildingFlag | 0xFFFFFFFFUL, InspectorSelectionType.Building)]
        [InlineData(InstanceIdConstants.StaticObjectFlag, InspectorSelectionType.StaticObject)]
        [InlineData(InstanceIdConstants.SceneryFlag | 123, InspectorSelectionType.Scenery)]
        [InlineData(InstanceIdConstants.PortalFlag | 999, InspectorSelectionType.Portal)]
        [InlineData(500UL, InspectorSelectionType.None)]
        public void GetType_CorrectlyIdentifiesType(ulong instanceId, InspectorSelectionType expected) {
            var result = InstanceIdConstants.GetType(instanceId);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(InstanceIdConstants.VertexFlag | 0x12345678UL, 0x12345678U)]
        [InlineData(0xFFFFFFFFUL, 0xFFFFFFFFU)]
        [InlineData(InstanceIdConstants.SceneryFlag, 0U)]
        [InlineData(12345UL, 12345U)]
        public void GetRawId_CorrectlyExtractsLower32Bits(ulong instanceId, uint expected) {
            var result = InstanceIdConstants.GetRawId(instanceId);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Flags_AreInUpper32Bits() {
            // All flags should be greater than uint.MaxValue
            Assert.True(InstanceIdConstants.VertexFlag > uint.MaxValue);
            Assert.True(InstanceIdConstants.BuildingFlag > uint.MaxValue);
            Assert.True(InstanceIdConstants.StaticObjectFlag > uint.MaxValue);
            Assert.True(InstanceIdConstants.SceneryFlag > uint.MaxValue);
            Assert.True(InstanceIdConstants.PortalFlag > uint.MaxValue);
        }

        [Fact]
        public void Flags_DoNotOverlap() {
            var flags = new[] {
                InstanceIdConstants.VertexFlag,
                InstanceIdConstants.BuildingFlag,
                InstanceIdConstants.StaticObjectFlag,
                InstanceIdConstants.SceneryFlag,
                InstanceIdConstants.PortalFlag
            };

            for (int i = 0; i < flags.Length; i++) {
                for (int j = i + 1; j < flags.Length; j++) {
                    Assert.Equal(0UL, flags[i] & flags[j]);
                }
            }
        }
    }
}
