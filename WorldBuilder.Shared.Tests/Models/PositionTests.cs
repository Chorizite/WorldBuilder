using System;
using System.Numerics;
using WorldBuilder.Shared.Models;
using Xunit;

namespace WorldBuilder.Shared.Tests.Models {
    public class PositionTests {
        [Fact]
        public void FromGlobal_DefaultRegion_CalculatesCorrectLandblock() {
            var pos = Position.FromGlobal(Vector3.Zero);
            
            Assert.Equal(0x7F7F, pos.LandblockId);
            Assert.Equal(127, pos.LandblockX);
            Assert.Equal(127, pos.LandblockY);
        }

        [Fact]
        public void NS_EW_CalculatesCorrectCoordinates() {
            var pos = Position.FromGlobal(Vector3.Zero);
            
            Assert.Equal(0.0f, pos.NS, 2);
            Assert.Equal(0.0f, pos.EW, 2);
        }

        [Theory]
        [InlineData("10.0N, 10.0E", 10.0f, 10.0f, 0f)]
        [InlineData("10.0S, 10.0W", -10.0f, -10.0f, 0f)]
        [InlineData("0.0N, 0.0E, 100.0z", 0.0f, 0.0f, 100.0f)]
        [InlineData("12.3N, 41.4E, 150z", 12.3f, 41.4f, 150f)]
        public void TryParse_MapCoordinateFormat_ParsesCorrectly(string input, float expectedNS, float expectedEW, float expectedZ) {
            bool success = Position.TryParse(input, out var pos);
            
            Assert.True(success);
            Assert.NotNull(pos);
            Assert.Equal(expectedNS, pos.NS, 2);
            Assert.Equal(expectedEW, pos.EW, 2);
            Assert.Equal(expectedZ, pos.LocalZ, 2);
        }

        [Fact]
        public void TryParse_LandblockFormat_ParsesCorrectly() {
            string input = "0x7D640014 [67.197197 95.557037 12.004999]";
            bool success = Position.TryParse(input, out var pos);

            Assert.True(success);
            Assert.NotNull(pos);
            Assert.Equal(0x7D64, pos.LandblockId);
            Assert.Equal(0x0014, pos.CellId);
            Assert.Equal(67.197197f, pos.LocalX, 4);
            Assert.Equal(95.557037f, pos.LocalY, 4);
            Assert.Equal(12.004999f, pos.LocalZ, 4);
            Assert.Null(pos.Rotation);
        }

        [Fact]
        public void TryParse_LandblockWithRotation_ParsesCorrectly() {
            string input = "0x7D640014 [67.197197 95.557037 12.004999] -0.521528 0.000000 0.000000 0.853234";
            bool success = Position.TryParse(input, out var pos);

            Assert.True(success);
            Assert.NotNull(pos);
            Assert.Equal(0x7D64, pos.LandblockId);
            Assert.NotNull(pos.Rotation);
            Assert.Equal(-0.521528f, pos.Rotation.Value.X, 6);
            Assert.Equal(0.000000f, pos.Rotation.Value.Y, 6);
            Assert.Equal(0.000000f, pos.Rotation.Value.Z, 6);
            Assert.Equal(0.853234f, pos.Rotation.Value.W, 6);
        }

        [Fact]
        public void TryParse_GameFormat_ParsesCorrectly() {
            string input = "Your location is: 0x7D640014 [67.197197 95.557037 12.004999] -0.521528 0.000000 0.000000 0.853234";
            bool success = Position.TryParse(input, out var pos);

            Assert.True(success);
            Assert.NotNull(pos);
            Assert.Equal(0x7D64, pos.LandblockId);
            Assert.Equal(0x0014, pos.CellId);
        }

        [Fact]
        public void LocalX_SettingValueExceedingBoundary_AdjustsLandblock() {
            var pos = new Position(0x8080, 1, 100f, 100f, 0f);
            pos.LocalX = 250f; 

            Assert.Equal(0x8180, pos.LandblockId);
            Assert.Equal(58f, pos.LocalX, 2);
        }

        [Fact]
        public void GlobalX_SettingValue_UpdatesLocalCoordinates() {
            var pos = new Position(0x8080, 1, 0f, 0f, 0f);
            float initialGlobalX = pos.GlobalX;
            
            pos.GlobalX += 500f;

            Assert.Equal(initialGlobalX + 500f, pos.GlobalX, 2);
            Assert.Equal(0x8280, pos.LandblockId);
            Assert.Equal(116f, pos.LocalX, 2);
        }

        [Fact]
        public void IsOutside_ChecksCellId() {
            var indoor = new Position(0x0001, 0x0100, 10f, 10f, 0f);
            var outdoor = new Position(0x8080, 0x0014, 10f, 10f, 0f);

            Assert.False(indoor.IsOutside);
            Assert.True(outdoor.IsOutside);
        }

        [Fact]
        public void DistanceTo_CalculatesCorrectDistance() {
            var p1 = Position.FromGlobal(new Vector3(0, 0, 0));
            var p2 = Position.FromGlobal(new Vector3(100, 100, 100));
            
            float expected = (float)Math.Sqrt(100*100 + 100*100 + 100*100);
            Assert.Equal(expected, p1.DistanceTo(p2), 2);
        }

        [Fact]
        public void DistanceToFlat_CalculatesCorrectDistance() {
            var p1 = Position.FromGlobal(new Vector3(0, 0, 0));
            var p2 = Position.FromGlobal(new Vector3(100, 100, 100));
            
            float expected = (float)Math.Sqrt(100*100 + 100*100);
            Assert.Equal(expected, p1.DistanceToFlat(p2), 2);
        }

        [Fact]
        public void HeadingTo_CalculatesCorrectHeading() {
            var p1 = Position.FromGlobal(new Vector3(0, 0, 0));
            var p2 = Position.FromGlobal(new Vector3(0, 100, 0)); // Due North
            
            Assert.Equal(0f, p1.HeadingTo(p2), 2);
            
            var p3 = Position.FromGlobal(new Vector3(100, 0, 0)); // Due East
            Assert.Equal(90f, p1.HeadingTo(p3), 2);
            
            var p4 = Position.FromGlobal(new Vector3(0, -100, 0)); // Due South
            Assert.Equal(180f, p1.HeadingTo(p4), 2);
            
            var p5 = Position.FromGlobal(new Vector3(-100, 0, 0)); // Due West
            Assert.Equal(270f, p1.HeadingTo(p5), 2);
        }

        [Fact]
        public void ToLandblockString_ReturnsCorrectFormat() {
            var pos = new Position(0x7D64, 0x0014, 67.197197f, 95.557037f, 12.004999f);
            string expected = "0x7D640014 [67.197197 95.557037 12.004999]";
            Assert.Equal(expected, pos.ToLandblockString());
        }

        [Fact]
        public void ToLandblockString_WithRotation_ReturnsCorrectFormat() {
            var pos = new Position(0x7D64, 0x0014, 67.197197f, 95.557037f, 12.004999f);
            pos.Rotation = new Quaternion(-0.521528f, 0.000000f, 0.000000f, 0.853234f);
            string expected = "0x7D640014 [67.197197 95.557037 12.004999] -0.521528 0.000000 0.000000 0.853234";
            Assert.Equal(expected, pos.ToLandblockString());
        }

        [Fact]
        public void Equals_NearlyEqualPositions_ReturnsTrue() {
            var p1 = new Position(0x8080, 1, 100.0001f, 100f, 0f);
            var p2 = new Position(0x8080, 1, 100.0002f, 100f, 0f);
            
            Assert.Equal(p1, p2);
        }

        [Fact]
        public void Equals_DifferentPositions_ReturnsFalse() {
            var p1 = new Position(0x8080, 1, 100.0f, 100f, 0f);
            var p2 = new Position(0x8081, 1, 100.0f, 100f, 0f);
            
            Assert.NotEqual(p1, p2);
        }

        [Fact]
        public void Equals_NearlyEqualRotations_ReturnsTrue() {
            var p1 = new Position(0x8080, 1, 100f, 100f, 0f);
            p1.Rotation = new Quaternion(0.1001f, 0.2f, 0.3f, 0.4f);
            
            var p2 = new Position(0x8080, 1, 100f, 100f, 0f);
            p2.Rotation = new Quaternion(0.1002f, 0.2f, 0.3f, 0.4f);
            
            Assert.Equal(p1, p2);
        }
    }
}
