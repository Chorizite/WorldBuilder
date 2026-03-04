using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Services;
using Xunit;

namespace WorldBuilder.Shared.Tests.Modules.Landscape.Tools {
    public class ObjectManipulationStickyZTests {
        [Fact]
        public void GetInterpolatedHeight_ShouldReturnCorrectHeight() {
            // Arrange
            var doc = CreateDocument();
            
            // Setup a chunk with specific heights
            var chunk = new LandscapeChunk(0);
            chunk.MergedEntries = new TerrainEntry[65 * 65];
            for (int i = 0; i < 65 * 65; i++) chunk.MergedEntries[i] = new TerrainEntry { Height = 0 };
            
            // vertex (0,0) -> index 0
            chunk.MergedEntries[0] = new TerrainEntry { Height = 10 };
            // vertex (1,0) -> index 1
            chunk.MergedEntries[1] = new TerrainEntry { Height = 20 };
            
            doc.LoadedChunks[0] = chunk;
            
            var region = Mock.Get(doc.Region!);
            var regionObj = doc.Region!.Region;
            var heightTable = new float[256];
            heightTable[10] = 100.0f;
            heightTable[20] = 200.0f;
            regionObj.LandDefs.LandHeightTable = heightTable;
            region.Setup(r => r.LandHeights).Returns(heightTable);

            // Act
            // (0, 0) should be exactly vertex (0,0) -> 100.0
            float h0 = doc.GetInterpolatedHeight(new Vector3(0, 0, 0));
            // (24, 0) should be exactly vertex (1,0) -> 200.0
            float h1 = doc.GetInterpolatedHeight(new Vector3(24, 0, 0));

            // Assert
            Assert.Equal(100.0f, h0);
            Assert.Equal(200.0f, h1);
            
            // Midpoint between (0,0) and (24,0) should be 150.0
            float hMid = doc.GetInterpolatedHeight(new Vector3(12, 0, 0));
            Assert.Equal(150.0f, hMid);
        }

        [Fact]
        public void GetGroundHeight_ShouldReturnCellHeight_WhenInCell() {
            // Arrange
            var doc = CreateDocument();
            var context = new LandscapeToolContext(doc, new Mock<IDatReaderWriter>().Object, new CommandHistory(), new Mock<ICamera>().Object, new Mock<ILogger>().Object);
            
            uint cellId = 0x12340101;
            context.GetEnvCellAt = (pos) => cellId;
            
            var cell = new Cell {
                Position = new float[] { 100, 200, 300, 1, 0, 0, 0 }
            };
            // Need to mock LoadedChunks to satisfy GetMergedEnvCell
            ushort chunkId = 0x0206; // (not used in mock but good to be realistic)
            var chunk = new LandscapeChunk(chunkId);
            doc.LoadedChunks[chunkId] = chunk;
            
            // Since we can't easily mock GetMergedEnvCell (it's not virtual), we rely on its internal behavior.
            // It calls CellDatabase.TryGet<EnvCell>(cellId, out var cell)
            var mockDb = Mock.Get(doc.CellDatabase!);
            var envCell = new DatReaderWriter.DBObjs.EnvCell {
                Position = new DatReaderWriter.Types.Frame { Origin = new Vector3(100, 200, 300) }
            };
            mockDb.Setup(db => db.TryGet<DatReaderWriter.DBObjs.EnvCell>(cellId, out envCell)).Returns(true);

            var tool = new ObjectManipulationTool();
            tool.Activate(context);

            // Act
            var getGroundHeight = tool.GetType().GetMethod("GetGroundHeight", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            float height = (float)getGroundHeight!.Invoke(tool, new object[] { new Vector3(500, 500, 500) })!;

            // Assert
            Assert.Equal(300.0f, height);
        }

        private LandscapeDocument CreateDocument() {
            var doc = new LandscapeDocument(1);
            typeof(LandscapeDocument).GetField("_didLoadRegionData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(doc, true);
            typeof(LandscapeDocument).GetField("_didLoadLayers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(doc, true);

            var regionMock = new Mock<ITerrainInfo>();
            regionMock.Setup(r => r.MapOffset).Returns(Vector2.Zero);
            regionMock.Setup(r => r.LandblockSizeInUnits).Returns(24f * 8f);
            regionMock.Setup(r => r.MapWidthInLandblocks).Returns(1);
            regionMock.Setup(r => r.MapHeightInLandblocks).Returns(1);
            regionMock.Setup(r => r.MapWidthInVertices).Returns(65);
            regionMock.Setup(r => r.MapHeightInVertices).Returns(65);
            regionMock.Setup(r => r.LandblockVerticeLength).Returns(9);
            regionMock.Setup(r => r.GetVertexIndex(It.IsAny<int>(), It.IsAny<int>()))
                .Returns((int x, int y) => y * 65 + x);
            
            var regionObj = new DatReaderWriter.DBObjs.Region();
            regionObj.LandDefs = new DatReaderWriter.Types.LandDefs();
            regionObj.LandDefs.LandHeightTable = new float[256];
            regionMock.Setup(r => r.Region).Returns(regionObj);

            doc.Region = regionMock.Object;
            doc.CellDatabase = new Mock<IDatDatabase>().Object;
            return doc;
        }
    }
}
