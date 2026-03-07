using Xunit;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Tests.Modules.Landscape {
    public class InstanceIdConstantsTests {

        [Theory]
        [InlineData(InspectorSelectionType.Vertex, ObjectState.Original, 0x00000001, 0x0000)]
        [InlineData(InspectorSelectionType.Building, ObjectState.Added, 0xFFFFFFFF, 0xFFFF)]
        [InlineData(InspectorSelectionType.StaticObject, ObjectState.Modified, 0x12345678, 0x1234)]
        [InlineData(InspectorSelectionType.Scenery, ObjectState.Deleted, 0x00000000, 0x0000)]
        [InlineData(InspectorSelectionType.Portal, ObjectState.Original, 0x0A0A0000, 0x0001)]
        [InlineData(InspectorSelectionType.EnvCell, ObjectState.Added, 0x0A0A0001, 0x0002)]
        [InlineData(InspectorSelectionType.EnvCellStaticObject, ObjectState.Modified, 0x0B0B0002, 0x0003)]
        public void Encode_CorrectlyCombinesFields(InspectorSelectionType type, ObjectState state, uint contextId, ushort index) {
            var result = InstanceIdConstants.Encode(type, state, contextId, index);

            Assert.Equal(type, InstanceIdConstants.GetType(result));
            Assert.Equal(state, InstanceIdConstants.GetState(result));
            Assert.Equal(contextId, InstanceIdConstants.GetContextId(result));
            Assert.Equal(index, InstanceIdConstants.GetObjectIndex(result));
        }

        [Fact]
        public void LegacyEncode_CorrectlyDefaultsStateAndIndex() {
            uint id = 12345;
            InspectorSelectionType type = InspectorSelectionType.Vertex;
            var result = InstanceIdConstants.Encode(id, type);

            Assert.Equal(type, InstanceIdConstants.GetType(result));
            Assert.Equal(ObjectState.Original, InstanceIdConstants.GetState(result));
            Assert.Equal(id, InstanceIdConstants.GetContextId(result));
            Assert.Equal(0, InstanceIdConstants.GetObjectIndex(result));
        }

        [Theory]
        [InlineData(0x12345678, 42, true)]
        [InlineData(0x87654321, 99, false)]
        public void EncodeEnvCellStaticObject_CorrectlyEncodes(uint cellId, ushort index, bool isCustom) {
            var result = InstanceIdConstants.EncodeEnvCellStaticObject(cellId, index, isCustom);

            Assert.Equal(InspectorSelectionType.EnvCellStaticObject, InstanceIdConstants.GetType(result));
            Assert.Equal(isCustom ? ObjectState.Added : ObjectState.Original, InstanceIdConstants.GetState(result));
            Assert.Equal(cellId, InstanceIdConstants.GetContextId(result));
            Assert.Equal(index, InstanceIdConstants.GetObjectIndex(result));
            Assert.Equal(isCustom, InstanceIdConstants.IsCustomObject(result));
        }

        [Fact]
        public void EncodeStaticObject_CorrectlyEncodes() {
            uint landblockId = 0x0A0A0000;
            ushort index = 5;

            var result = InstanceIdConstants.EncodeStaticObject(landblockId, index);

            Assert.Equal(InspectorSelectionType.StaticObject, InstanceIdConstants.GetType(result));
            Assert.Equal(ObjectState.Original, InstanceIdConstants.GetState(result));
            Assert.Equal(landblockId, InstanceIdConstants.GetContextId(result));
            Assert.Equal(index, InstanceIdConstants.GetObjectIndex(result));
        }

        [Fact]
        public void EncodeBuilding_CorrectlyEncodes() {
            uint landblockId = 0x12340000;
            ushort index = 10;

            var result = InstanceIdConstants.EncodeBuilding(landblockId, index);

            Assert.Equal(InspectorSelectionType.Building, InstanceIdConstants.GetType(result));
            Assert.Equal(ObjectState.Original, InstanceIdConstants.GetState(result));
            Assert.Equal(landblockId, InstanceIdConstants.GetContextId(result));
            Assert.Equal(index, InstanceIdConstants.GetObjectIndex(result));
        }

        [Fact]
        public void LegacyAliases_FunctionCorrectly() {
            var id = InstanceIdConstants.Encode(InspectorSelectionType.EnvCell, ObjectState.Added, 0x11112222, 0x3333);

            Assert.Equal(0x11112222u, InstanceIdConstants.GetRawId(id));
            Assert.Equal(0x3333, InstanceIdConstants.GetSecondaryId(id));
            Assert.True(InstanceIdConstants.IsCustomObject(id));
        }
    }
}