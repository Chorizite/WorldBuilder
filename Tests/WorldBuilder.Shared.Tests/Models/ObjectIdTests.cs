using WorldBuilder.Shared.Models;
using Xunit;

namespace WorldBuilder.Shared.Tests.Models {
    public class ObjectIdTests {
        [Fact]
        public void Id32_CorrectlyReconstructs32BitIdForDatObject() {
            // Landblock 0x1234, Cell 0x0101 -> CellID 0x12340101
            uint context = 0x1234;
            ushort index = 0x0101;
            var id = ObjectId.FromDat(ObjectType.EnvCell, 0, context, index);

            Assert.Equal(0x12340101u, id.DataId);
        }

        [Fact]
        public void Id32_CorrectlyReconstructs32BitIdForDbObject() {
            uint context = 0x12340101; // Full cell ID
            var id = ObjectId.NewDb(ObjectType.EnvCellStaticObject, context);
            
            // For EnvCellStaticObject, Id32 should be the full Context
            Assert.Equal(context, id.DataId);
        }

        [Fact]
        public void Id32_ReturnsZeroForNonCellTypes() {
            var id = ObjectId.FromDat(ObjectType.StaticObject, 0, 0x1234, 1);
            Assert.Equal(0u, id.DataId);
        }

        [Fact]
        public void Id32_HandlesLargeContextAndIndex() {
            uint context = 0xFFFF;
            ushort index = 0xFFFF;
            var id = ObjectId.FromDat(ObjectType.EnvCell, 0, context, index);

            Assert.Equal(0xFFFFFFFFu, id.DataId);
        }
    }
}
