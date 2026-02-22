using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Services;
using Xunit;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;

namespace WorldBuilder.Shared.Tests.Modules.Landscape {
    public class LandscapeDocumentMergingTests {
        private readonly Mock<ITerrainInfo> _mockRegion;
        private readonly Mock<IDatDatabase> _mockCellDatabase;
        private readonly LandscapeDocument _doc;
        private readonly uint _regionId = 1;

        public LandscapeDocumentMergingTests() {
            _mockRegion = new Mock<ITerrainInfo>();
            _mockCellDatabase = new Mock<IDatDatabase>();
            
            _doc = new LandscapeDocument(_regionId);
            _doc.Region = _mockRegion.Object;
            _doc.CellDatabase = _mockCellDatabase.Object;

            // Bypass dats loading
            typeof(LandscapeDocument).GetField("_didLoadRegionData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(_doc, true);
            typeof(LandscapeDocument).GetField("_didLoadLayers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(_doc, true);
        }

        [Fact]
        public void GetMergedLandblock_WithStaticObjectInLayer_ReturnsMergedObject() {
            // Arrange
            uint landblockId = 0x12340000;
            string layerId = "Layer1";
            _doc.AddLayer([], "Layer 1", false, layerId);
            
            // Mock chunk
            ushort chunkId = 0x0206; // (0x12/8=2, 0x34/8=6)
            _mockRegion.Setup(r => r.GetLandblockId(It.IsAny<int>(), It.IsAny<int>())).Returns((ushort)0x1234);
            
            var chunk = new LandscapeChunk(chunkId);
            chunk.EditsDetached = new LandscapeChunkDocument(LandscapeChunkDocument.GetId(_regionId, chunk.ChunkX, chunk.ChunkY));
            _doc.LoadedChunks[chunkId] = chunk;

            var obj = new StaticObject {
                SetupId = 0x01000001,
                Position = new float[] { 1.0f, 2.0f, 3.0f, 1.0f, 0.0f, 0.0f, 0.0f },
                InstanceId = 100,
                LayerId = layerId
            };

            // Act
            _doc.AddStaticObject(layerId, landblockId, obj);
            var merged = _doc.GetMergedLandblock(landblockId);

            // Assert
            Assert.Contains(merged.StaticObjects, o => o.InstanceId == 100 && o.SetupId == 0x01000001);
        }

        [Fact]
        public void GetMergedLandblock_WithHiddenLayer_DoesNotReturnObject() {
            // Arrange
            uint landblockId = 0x00000000;
            string layerId = "Layer1";
            _doc.AddLayer([], "Layer 1", false, layerId);
            var layer = (LandscapeLayer)_doc.FindItem(layerId)!;
            layer.IsVisible = false;
            
            ushort chunkId = 0; 
            var chunk = new LandscapeChunk(chunkId);
            chunk.EditsDetached = new LandscapeChunkDocument(LandscapeChunkDocument.GetId(_regionId, chunk.ChunkX, chunk.ChunkY));
            _doc.LoadedChunks[chunkId] = chunk;

            var obj = new StaticObject { InstanceId = 100, LayerId = layerId };
            _doc.AddStaticObject(layerId, landblockId, obj);

            // Act
            var merged = _doc.GetMergedLandblock(landblockId);

            // Assert
            Assert.DoesNotContain(merged.StaticObjects, o => o.InstanceId == 100);
        }

        [Fact]
        public void GetMergedLandblock_TombstonesBaseObject() {
            // Arrange
            uint landblockId = 0x12340000;
            uint lbFileId = (landblockId & 0xFFFF0000) | 0xFFFE;
            
            // Mock base object in DAT
            var lbi = new LandBlockInfo();
            lbi.Objects.Add(new Stab { Id = 0x1234, Frame = new Frame() });
            
            byte[]? dummyBytes = new byte[1];
            _mockCellDatabase.Setup(db => db.TryGetFileBytes(lbFileId, out dummyBytes)).Returns(true);
            _mockCellDatabase.Setup(db => db.TryGet<LandBlockInfo>(lbFileId, out lbi)).Returns(true);

            string layerId = "Layer1";
            _doc.AddLayer([], "Layer 1", false, layerId);
            
            ushort chunkId = 0x0206; 
            var chunk = new LandscapeChunk(chunkId);
            chunk.EditsDetached = new LandscapeChunkDocument(LandscapeChunkDocument.GetId(_regionId, chunk.ChunkX, chunk.ChunkY));
            _doc.LoadedChunks[chunkId] = chunk;

            // Delete base object (InstanceId 0)
            _doc.RemoveInstance(layerId, chunkId, 0);

            // Act
            var merged = _doc.GetMergedLandblock(landblockId);

            // Assert
            Assert.Empty(merged.StaticObjects);
        }

        [Fact]
        public void GetMergedLandblock_WithBuildingInLayer_ReturnsMergedBuilding() {
            // Arrange
            uint landblockId = 0x12340000;
            string layerId = "Layer1";
            _doc.AddLayer([], "Layer 1", false, layerId);
            
            ushort chunkId = 0x0206;
            var chunk = new LandscapeChunk(chunkId);
            chunk.EditsDetached = new LandscapeChunkDocument(LandscapeChunkDocument.GetId(_regionId, chunk.ChunkX, chunk.ChunkY));
            _doc.LoadedChunks[chunkId] = chunk;

            var bldg = new BuildingObject {
                ModelId = 0x01000002,
                Position = new float[] { 4.0f, 5.0f, 6.0f, 1.0f, 0.0f, 0.0f, 0.0f },
                InstanceId = 200,
                LayerId = layerId
            };

            var layerEdits = new LandscapeChunkEdits();
            layerEdits.Buildings[landblockId] = new List<BuildingObject> { bldg };
            chunk.EditsDetached.LayerEdits[layerId] = layerEdits;

            // Act
            var merged = _doc.GetMergedLandblock(landblockId);

            // Assert
            Assert.Contains(merged.Buildings, b => b.InstanceId == 200 && b.ModelId == 0x01000002);
        }

        [Fact]
        public void GetMergedEnvCell_ReturnsMergedCell() {
            // Arrange
            uint cellId = 0x12340101;
            var cell = new EnvCell {
                EnvironmentId = 10,
                CellStructure = 5,
                Position = new Frame { Origin = new Vector3(1, 2, 3) },
                Surfaces = new List<ushort> { 1, 2, 3 },
                CellPortals = new List<CellPortal>()
            };

            _mockCellDatabase.Setup(db => db.TryGet<EnvCell>(cellId, out cell)).Returns(true);

            string layerId = "Layer1";
            _doc.AddLayer([], "Layer 1", false, layerId);
            
            ushort chunkId = 0x0206;
            var chunk = new LandscapeChunk(chunkId);
            chunk.EditsDetached = new LandscapeChunkDocument(LandscapeChunkDocument.GetId(_regionId, chunk.ChunkX, chunk.ChunkY));
            _doc.LoadedChunks[chunkId] = chunk;

            // Add an object to this cell in the layer
            var obj = new StaticObject { InstanceId = 500, SetupId = 0x01000005, LayerId = layerId };
            
            var layerEdits = new LandscapeChunkEdits();
            layerEdits.Cells[cellId] = new Cell { 
                StaticObjects = new List<StaticObject> { obj },
                LayerId = layerId,
                EnvironmentId = 10
            };
            chunk.EditsDetached.LayerEdits[layerId] = layerEdits;

            // Act
            var merged = _doc.GetMergedEnvCell(cellId);

            // Assert
            Xunit.Assert.Equal((uint)10, merged.EnvironmentId);
            Assert.Contains(merged.StaticObjects, o => o.InstanceId == 500);
        }
    }
}
