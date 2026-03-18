using Moq;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Services;
using WorldBuilder.Shared.Services;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using Xunit;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Shared.Tests.Modules.Landscape.Services {
    public class LandscapeObjectServiceTests {
        private readonly Mock<IWorldCoordinateService> _mockWorldCoords;
        private readonly LandscapeObjectService _service;

        public LandscapeObjectServiceTests() {
            _mockWorldCoords = new Mock<IWorldCoordinateService>();
            _service = new LandscapeObjectService(_mockWorldCoords.Object);
        }

        [Fact]
        public void ComputeWorldPosition_CalculatesCorrectly() {
            var mockRegion = new Mock<ITerrainInfo>();
            mockRegion.Setup(r => r.LandblockSizeInUnits).Returns(192f);
            mockRegion.Setup(r => r.MapOffset).Returns(new Vector2(100, 200));

            // Landblock 0x1234 -> lbX=0x12 (18), lbY=0x34 (52)
            ushort landblockId = 0x1234;
            Vector3 localPos = new Vector3(10, 20, 5);

            var worldPos = _service.ComputeWorldPosition(mockRegion.Object, landblockId, localPos);

            Assert.Equal(18 * 192f + 100 + 10, worldPos.X);
            Assert.Equal(52 * 192f + 200 + 20, worldPos.Y);
            Assert.Equal(5, worldPos.Z);
        }

        [Fact]
        public void ComputeLandblockId_CalculatesCorrectly() {
            var mockRegion = new Mock<ITerrainInfo>();
            mockRegion.Setup(r => r.LandblockSizeInUnits).Returns(192f);
            mockRegion.Setup(r => r.MapOffset).Returns(new Vector2(100, 200));

            Vector3 worldPos = new Vector3(18 * 192f + 100 + 50, 52 * 192f + 200 + 60, 0);

            var lbId = _service.ComputeLandblockId(mockRegion.Object, worldPos);

            Assert.Equal((ushort)0x1234, lbId);
        }

        [Fact]
        public void GetStaticObjectLayerId_ReturnsCorrectLayerForStaticObject() {
            var doc = new LandscapeDocument(1);
            ushort landblockId = 0x1234;
            var instanceId = ObjectId.FromDat(ObjectType.StaticObject, 0, landblockId, 1001); 
            
            var lb = new MergedLandblock {
                StaticObjects = { [instanceId] = new StaticObject { InstanceId = instanceId, LayerId = "TestLayer" } }
            };
            
            var mockCache = new Mock<ILandscapeCacheService>();
            var outLb = lb;
            mockCache.Setup(c => c.TryGetLandblock(doc.Id, landblockId, out outLb)).Returns(true);
            
            var mockDocManager = new Mock<IDocumentManager>();
            mockDocManager.Setup(m => m.LandscapeCacheService).Returns(mockCache.Object);
            
            typeof(LandscapeDocument).GetField("_documentManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(doc, mockDocManager.Object);

            var layerId = _service.GetStaticObjectLayerId(doc, landblockId, instanceId);

            Assert.Equal("TestLayer", layerId);
        }

        [Fact]
        public async Task ResolveCellIdAsync_ReturnsCellIdWhenFound() {
            var doc = new LandscapeDocument(1);
            Vector3 pos = new Vector3(500, 600, 0);
            
            var mockRegion = new Mock<ITerrainInfo>();
            mockRegion.Setup(r => r.MapOffset).Returns(new Vector2(0, 0));
            mockRegion.Setup(r => r.LandblockSizeInUnits).Returns(192f);
            mockRegion.Setup(r => r.MapWidthInLandblocks).Returns(256);
            mockRegion.Setup(r => r.MapHeightInLandblocks).Returns(256);
            doc.Region = mockRegion.Object;
            
            var mockCellDb = new Mock<IDatDatabase>();
            doc.CellDatabase = mockCellDb.Object;

            var mockDocManager = new Mock<IDocumentManager>();
            var mockCache = new Mock<ILandscapeCacheService>();
            var mockDataProvider = new Mock<ILandscapeDataProvider>();
            
            mockDocManager.Setup(m => m.LandscapeCacheService).Returns(mockCache.Object);
            mockDocManager.Setup(m => m.LandscapeDataProvider).Returns(mockDataProvider.Object);

            mockCache.Setup(m => m.GetOrAddLandblockAsync(It.IsAny<string>(), It.IsAny<ushort>(), It.IsAny<Func<Task<MergedLandblock>>>()))
                .Returns<string, ushort, Func<Task<MergedLandblock>>>((id, lbId, factory) => factory());
            mockCache.Setup(m => m.GetOrAddEnvCellAsync(It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<Func<Task<Cell>>>()))
                .Returns<string, uint, Func<Task<Cell>>>((id, cellId, factory) => factory());
            
            typeof(LandscapeDocument).GetField("_documentManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(doc, mockDocManager.Object);
            typeof(LandscapeDocument).GetField("_landscapeDataProvider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(doc, mockDataProvider.Object);

            // Mock GetMergedLandblocksAsync
            mockDataProvider.Setup(p => p.GetMergedLandblocksAsync(It.IsAny<IEnumerable<ushort>>(), It.IsAny<IDatDatabase>(), It.IsAny<IDatDatabase>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<ushort, MergedLandblock> { 
                    [(ushort)0x0203] = new MergedLandblock { EnvCellIds = [0x12340101u] } 
                });
            
            // Mock GetMergedEnvCellAsync
            mockDataProvider.Setup(p => p.GetMergedEnvCellAsync(0x12340101u, It.IsAny<IDatDatabase>(), It.IsAny<IDatDatabase>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Cell { 
                    MinBounds = new Vector3(100, 10, -100), 
                    MaxBounds = new Vector3(130, 40, 100) 
                });

            var cellId = await _service.ResolveCellIdAsync(doc, pos);

            Assert.Equal(0x12340101u, cellId);
        }

        [Fact]
        public async Task ResolveCellIdAsync_StickyLogic_ReturnsNullWhenNotFound() {
            var doc = new LandscapeDocument(1);
            Vector3 pos = new Vector3(500, 600, 0);
            uint startCellId = 0x12340101u;
            
            var mockRegion = new Mock<ITerrainInfo>();
            mockRegion.Setup(r => r.MapOffset).Returns(new Vector2(0, 0));
            mockRegion.Setup(r => r.LandblockSizeInUnits).Returns(192f);
            mockRegion.Setup(r => r.MapWidthInLandblocks).Returns(256);
            mockRegion.Setup(r => r.MapHeightInLandblocks).Returns(256);
            doc.Region = mockRegion.Object;
            
            var mockCellDb = new Mock<IDatDatabase>();
            doc.CellDatabase = mockCellDb.Object;

            var mockDocManager = new Mock<IDocumentManager>();
            var mockCache = new Mock<ILandscapeCacheService>();
            var mockDataProvider = new Mock<ILandscapeDataProvider>();
            
            mockDocManager.Setup(m => m.LandscapeCacheService).Returns(mockCache.Object);
            mockDocManager.Setup(m => m.LandscapeDataProvider).Returns(mockDataProvider.Object);

            mockCache.Setup(m => m.GetOrAddLandblockAsync(It.IsAny<string>(), It.IsAny<ushort>(), It.IsAny<Func<Task<MergedLandblock>>>()))
                .Returns<string, ushort, Func<Task<MergedLandblock>>>((id, lbId, factory) => factory());
            mockCache.Setup(m => m.GetOrAddEnvCellAsync(It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<Func<Task<Cell>>>()))
                .Returns<string, uint, Func<Task<Cell>>>((id, cellId, factory) => factory());
            
            typeof(LandscapeDocument).GetField("_documentManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(doc, mockDocManager.Object);
            typeof(LandscapeDocument).GetField("_landscapeDataProvider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(doc, mockDataProvider.Object);

            mockDataProvider.Setup(p => p.GetMergedLandblocksAsync(It.IsAny<IEnumerable<ushort>>(), It.IsAny<IDatDatabase>(), It.IsAny<IDatDatabase>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<ushort, MergedLandblock>());

            var cellId = await _service.ResolveCellIdAsync(doc, pos, startCellId);

            Assert.Null(cellId);
        }
    }
}
