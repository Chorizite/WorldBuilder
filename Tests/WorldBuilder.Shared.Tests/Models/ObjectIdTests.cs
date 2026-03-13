using Xunit;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Tests.Models {
    public class ObjectIdTests {

        [Theory]
        [InlineData(ObjectType.StaticObject, 0, 0x12345678u, 0x1234)]
        [InlineData(ObjectType.Building, 1, 0xFFFFFFFFu, 0xFFFF)]
        [InlineData(ObjectType.Scenery, 0, 0x00000000u, 0x0000)]
        [InlineData(ObjectType.Portal, 0, 0x0A0A0000u, 0x0001)]
        [InlineData(ObjectType.EnvCell, 0, 0x0A0A0001u, 0x0002)]
        [InlineData(ObjectType.EnvCellStaticObject, 12, 0x0B0B0002u, 0x0003)]
        public void FromDat_CorrectlyEncodes(ObjectType type, byte state, uint context, ushort index) {
            var id = ObjectId.FromDat(type, state, context, index);

            Assert.True(id.IsDat);
            Assert.False(id.IsDb);
            Assert.Equal(type, id.Type);
            Assert.Equal(state, id.State);
            Assert.Equal(context, id.Context);
            Assert.Equal(index, id.Index);
        }

        [Fact]
        public void NewDb_CorrectlyEncodes() {
            var type = ObjectType.StaticObject;
            uint context = 0x12345678;
            var id = ObjectId.NewDb(type, context);

            Assert.True(id.IsDb);
            Assert.False(id.IsDat);
            Assert.Equal(type, id.Type);
            Assert.Equal(context, id.Context);
            Assert.NotEqual(0UL, id.Low & 0xFFFFFFFFUL); // Random part
        }

        [Fact]
        public void Parse_Dat_CorrectlyParses() {
            var s = "dat:StaticObject:12345678:ABCD:5";
            var id = ObjectId.Parse(s);

            Assert.True(id.IsDat);
            Assert.Equal(ObjectType.StaticObject, id.Type);
            Assert.Equal(0x12345678u, id.Context);
            Assert.Equal(0xABCDu, id.Index);
            Assert.Equal(5, id.State);
            Assert.Equal(s, id.ToString());
        }

        [Fact]
        public void Parse_Db_CorrectlyParses() {
            var type = ObjectType.Building;
            var low = 0x1111222233334444UL;
            var high = 0x5555666677778888UL;
            // Force DB flag and type for the string we'll manually construct
            high |= (1UL << 63);
            high &= ~(0xFFFFUL << 47);
            high |= ((ulong)type << 47);

            var hex = $"{high:X16}{low:X16}";
            var s = $"db:{type}:{hex}";
            var id = ObjectId.Parse(s);

            Assert.True(id.IsDb);
            Assert.Equal(type, id.Type);
            Assert.Equal(high, id.High);
            Assert.Equal(low, id.Low);
            Assert.Equal(s, id.ToString());
        }

        [Fact]
        public void Parse_Db_Legacy_CorrectlyParses() {
            var type = ObjectType.Building;
            var oldId = 0x020100007D64FFFFUL; // Type=2, State=1, Context=0x00007D64, Index=0xFFFF
            var id = ObjectId.FromLegacyDbId(type, oldId);
            var s = id.ToString();

            Assert.True(id.IsDb);
            Assert.Equal(type, id.Type);
            Assert.Equal(1, id.State);
            Assert.Equal(0x00007D64u, id.Context);
            Assert.Equal(0xFFFFu, id.Index);
            
            var parsed = ObjectId.Parse(s);
            Assert.Equal(id, parsed);
        }

        [Fact]
        public void Empty_IsCorrect() {
            var id = ObjectId.Empty;
            Assert.True(id.IsEmpty);
            Assert.Equal("empty", id.ToString());
        }

        [Fact]
        public void Equality_Works() {
            var id1 = ObjectId.FromDat(ObjectType.Scenery, 0, 1, 2);
            var id2 = ObjectId.FromDat(ObjectType.Scenery, 0, 1, 2);
            var id3 = ObjectId.FromDat(ObjectType.Scenery, 0, 1, 3);

            Assert.Equal(id1, id2);
            Assert.NotEqual(id1, id3);
            Assert.True(id1 == id2);
            Assert.True(id1 != id3);
        }
    }
}
