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
        [InlineData(100, InspectorSelectionType.EnvCell, InstanceIdConstants.EnvCellFlag | 100UL)]
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
        [InlineData(InstanceIdConstants.EnvCellFlag | 0x12345678UL, InspectorSelectionType.EnvCell)]
        [InlineData(InstanceIdConstants.EnvCellStaticObjectFlag | 0x12345678UL, InspectorSelectionType.EnvCellStaticObject)]
        [InlineData(500UL, InspectorSelectionType.None)]
        public void GetType_CorrectlyIdentifiesType(ulong instanceId, InspectorSelectionType expected) {
            var result = InstanceIdConstants.GetType(instanceId);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(0x12345678U, 5, false)]
        [InlineData(0x12345678U, 0x7FFF, true)]
        public void EncodeEnvCellStaticObject_CorrectlyEncodes(uint cellId, ushort index, bool isCustom) {
            var result = InstanceIdConstants.EncodeEnvCellStaticObject(cellId, index, isCustom);
            Assert.Equal(InspectorSelectionType.EnvCellStaticObject, InstanceIdConstants.GetType(result));
            Assert.Equal(cellId, InstanceIdConstants.GetRawId(result));
            Assert.Equal(index, InstanceIdConstants.GetSecondaryId(result));
            Assert.Equal(isCustom, InstanceIdConstants.IsCustomObject(result));
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
            Assert.True(InstanceIdConstants.EnvCellFlag > uint.MaxValue);
            Assert.True(InstanceIdConstants.EnvCellStaticObjectFlag > uint.MaxValue);
        }

        [Fact]
        public void Flags_DoNotOverlap() {
            var flags = new[] {
                InstanceIdConstants.VertexFlag,
                InstanceIdConstants.BuildingFlag,
                InstanceIdConstants.StaticObjectFlag,
                InstanceIdConstants.SceneryFlag,
                InstanceIdConstants.PortalFlag,
                InstanceIdConstants.EnvCellFlag,
                InstanceIdConstants.EnvCellStaticObjectFlag
            };

            for (int i = 0; i < flags.Length; i++) {
                for (int j = i + 1; j < flags.Length; j++) {
                    Assert.Equal(0UL, flags[i] & flags[j]);
                }
            }
        }

        [Theory]
        [InlineData(0x0A0A0000u, 0, InspectorSelectionType.StaticObject)]
        [InlineData(0x12340000u, 5, InspectorSelectionType.StaticObject)]
        [InlineData(0xFFFF0000u, 0xFFFF, InspectorSelectionType.StaticObject)]
        public void EncodeStaticObject_CorrectlyEncodesLandblockAndIndex(uint landblockId, ushort index, InspectorSelectionType expectedType) {
            var result = InstanceIdConstants.EncodeStaticObject(landblockId, index);

            Assert.Equal(expectedType, InstanceIdConstants.GetType(result));
            Assert.Equal(index, InstanceIdConstants.GetObjectIndex(result));
            Assert.Equal((ushort)((landblockId >> 16) & 0xFFFF), InstanceIdConstants.GetLandblockPrefix(result));
        }

        [Theory]
        [InlineData(0x0A0A0000u, 0, InspectorSelectionType.Building)]
        [InlineData(0x12340000u, 3, InspectorSelectionType.Building)]
        public void EncodeBuilding_CorrectlyEncodesLandblockAndIndex(uint landblockId, ushort index, InspectorSelectionType expectedType) {
            var result = InstanceIdConstants.EncodeBuilding(landblockId, index);

            Assert.Equal(expectedType, InstanceIdConstants.GetType(result));
            Assert.Equal(index, InstanceIdConstants.GetObjectIndex(result));
            Assert.Equal((ushort)((landblockId >> 16) & 0xFFFF), InstanceIdConstants.GetLandblockPrefix(result));
        }

        [Fact]
        public void EncodeStaticObject_DifferentLandblocks_ProduceUniqueIds() {
            // Two different landblocks, same array index — must produce different InstanceIds
            uint lbA = (10u << 24) | (10u << 16) | 0xFFFE;
            uint lbB = (11u << 24) | (11u << 16) | 0xFFFE;

            var idA = InstanceIdConstants.EncodeStaticObject(lbA, 0);
            var idB = InstanceIdConstants.EncodeStaticObject(lbB, 0);

            Assert.NotEqual(idA, idB);
        }

        [Fact]
        public void GetObjectIndex_ExtractsLower16Bits() {
            var id = InstanceIdConstants.EncodeStaticObject(0x12340000u, 42);
            Assert.Equal((ushort)42, InstanceIdConstants.GetObjectIndex(id));
        }

        [Fact]
        public void GetLandblockPrefix_ExtractsBits16To31() {
            var id = InstanceIdConstants.EncodeStaticObject(0x12340000u, 0);
            Assert.Equal((ushort)0x1234, InstanceIdConstants.GetLandblockPrefix(id));
        }
    }
}
