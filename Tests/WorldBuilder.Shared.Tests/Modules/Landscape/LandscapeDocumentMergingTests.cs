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

            // Delete base object — use landblock-aware encoding to match what GetMergedLandblock generates
            _doc.RemoveInstance(layerId, chunkId, InstanceIdConstants.EncodeStaticObject(lbFileId, 0));

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

        [Fact]
        public void GetMergedLandblock_DeleteInOneLandblock_DoesNotAffectOtherInSameChunk() {
            // Arrange: two landblocks in the same chunk, each with a base object at index 0
            uint lbA = 0x10100000;
            uint lbB = 0x11100000;
            uint lbFileIdA = (lbA & 0xFFFF0000) | 0xFFFE;
            uint lbFileIdB = (lbB & 0xFFFF0000) | 0xFFFE;

            // Both are in chunk (0x10/8=2, 0x10/8=2) = 0x0202
            ushort chunkId = 0x0202;

            // Mock base objects for landblock A
            var lbiA = new LandBlockInfo();
            lbiA.Objects.Add(new Stab { Id = 0x01000001, Frame = new Frame() });
            byte[]? dummyBytesA = new byte[1];
            _mockCellDatabase.Setup(db => db.TryGetFileBytes(lbFileIdA, out dummyBytesA)).Returns(true);
            _mockCellDatabase.Setup(db => db.TryGet<LandBlockInfo>(lbFileIdA, out lbiA)).Returns(true);

            // Mock base objects for landblock B
            var lbiB = new LandBlockInfo();
            lbiB.Objects.Add(new Stab { Id = 0x01000002, Frame = new Frame() });
            byte[]? dummyBytesB = new byte[1];
            _mockCellDatabase.Setup(db => db.TryGetFileBytes(lbFileIdB, out dummyBytesB)).Returns(true);
            _mockCellDatabase.Setup(db => db.TryGet<LandBlockInfo>(lbFileIdB, out lbiB)).Returns(true);

            string layerId = "Layer1";
            _doc.AddLayer([], "Layer 1", false, layerId);

            var chunk = new LandscapeChunk(chunkId);
            chunk.EditsDetached = new LandscapeChunkDocument(LandscapeChunkDocument.GetId(_regionId, chunk.ChunkX, chunk.ChunkY));
            _doc.LoadedChunks[chunkId] = chunk;

            // Delete base object #0 in landblock A only
            _doc.RemoveInstance(layerId, chunkId, InstanceIdConstants.EncodeStaticObject(lbFileIdA, 0));

            // Act
            var mergedA = _doc.GetMergedLandblock(lbA);
            var mergedB = _doc.GetMergedLandblock(lbB);

            // Assert: A's object is deleted, B's object is NOT affected
            Assert.Empty(mergedA.StaticObjects);
            Assert.Single(mergedB.StaticObjects);
            Assert.Equal(0x01000002u, mergedB.StaticObjects[0].SetupId);
        }
    }
}
