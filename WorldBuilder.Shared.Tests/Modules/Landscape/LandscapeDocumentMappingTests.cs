using System;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using Xunit;
using Moq;
using System.Numerics;

namespace WorldBuilder.Shared.Tests.Modules.Landscape {
    public class LandscapeDocumentMappingTests {
        private readonly Mock<ITerrainInfo> _mockRegion;
        private readonly LandscapeDocument _doc;

        public LandscapeDocumentMappingTests() {
            _mockRegion = new Mock<ITerrainInfo>();
            _mockRegion.Setup(r => r.MapWidthInVertices).Returns(1025); // 128 landblocks * 8 + 1
            _mockRegion.Setup(r => r.MapHeightInVertices).Returns(1025);
            _mockRegion.Setup(r => r.MapWidthInLandblocks).Returns(128);
            _mockRegion.Setup(r => r.MapHeightInLandblocks).Returns(128);
            _mockRegion.Setup(r => r.LandblockVerticeLength).Returns(9);
            
            _doc = new LandscapeDocument(1);
            _doc.Region = _mockRegion.Object;
        }

        [Fact]
        public void GetLocalVertexIndex_Origin() {
            // Act
            var (chunkId, localIndex) = _doc.GetLocalVertexIndex(0);

            // Assert
            Assert.Equal(0, chunkId);
            Assert.Equal(0, localIndex);
        }

        [Fact]
        public void GetLocalVertexIndex_BoundaryOfFirstChunk() {
            var (chunkId, localIndex) = _doc.GetLocalVertexIndex(64);

            Assert.Equal(LandscapeChunk.GetId(1, 0), chunkId);
            Assert.Equal(0, localIndex);
        }

        [Fact]
        public void GetLocalVertexIndex_LastVertexOfFirstChunk() {
            // Vertex at (63, 0)
            var (chunkId, localIndex) = _doc.GetLocalVertexIndex(63);

            Assert.Equal(0, chunkId);
            Assert.Equal(63, localIndex);
        }

        [Fact]
        public void GetGlobalVertexIndex_RoundTrip() {
            uint originalGlobalIndex = 1025 * 70 + 80; // (80, 70)
            
            var (chunkId, localIndex) = _doc.GetLocalVertexIndex(originalGlobalIndex);
            uint resultGlobalIndex = _doc.GetGlobalVertexIndex(chunkId, localIndex);

            Assert.Equal(originalGlobalIndex, resultGlobalIndex);
        }

        [Fact]
        public void GetAffectedLandblocks_SingleVertex_NonBoundary() {
            // Vertex (4, 4) is in Landblock (0, 0)
            // stride = 8
            uint globalVertexIndex = (uint)(4 * 1025 + 4);
            
            var blocks = _doc.GetAffectedLandblocks([globalVertexIndex]);

            Assert.Single(blocks);
            Assert.Contains((0, 0), blocks);
        }

        [Fact]
        public void GetAffectedLandblocks_LandblockBoundary_ReturnsFourBlocks() {
            // Vertex (8, 8) is on the corner of 4 landblocks
            uint globalVertexIndex = (uint)(8 * 1025 + 8);
            
            var blocks = _doc.GetAffectedLandblocks([globalVertexIndex]).ToList();

            Assert.Equal(4, blocks.Count);
            Assert.Contains((0, 0), blocks);
            Assert.Contains((1, 0), blocks);
            Assert.Contains((0, 1), blocks);
            Assert.Contains((1, 1), blocks);
        }
    }
}
