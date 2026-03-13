using Moq;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Services;
using WorldBuilder.Shared.Repositories;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace WorldBuilder.Shared.Tests.Commands.Landscape;

public class StaticObjectCommandTests {
    private readonly Mock<IDocumentManager> _mockDocManager;
    private readonly Mock<IDatReaderWriter> _mockDats;
    private readonly Mock<ITransaction> _mockTx;
    private readonly string _terrainDocId = "LandscapeDocument_1";

    private readonly Mock<IProjectRepository> _mockRepo;

    public StaticObjectCommandTests() {
        _mockDocManager = new Mock<IDocumentManager>();
        _mockDocManager.Setup(m => m.GetLayersAsync(It.IsAny<uint>(), It.IsAny<ITransaction?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<LandscapeLayerBase>());
        _mockDats = new Mock<IDatReaderWriter>();
        _mockTx = new Mock<ITransaction>();
        _mockRepo = new Mock<IProjectRepository>();

        _mockDocManager.Setup(m => m.ProjectRepository).Returns(_mockRepo.Object);
        _mockDocManager.Setup(m => m.UpsertStaticObjectAsync(It.IsAny<StaticObject>(), It.IsAny<uint>(), It.IsAny<ushort?>(), It.IsAny<uint?>(), It.IsAny<ITransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Unit>.Success(Unit.Value));
        _mockDocManager.Setup(m => m.DeleteStaticObjectAsync(It.IsAny<ObjectId>(), It.IsAny<ITransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Unit>.Success(Unit.Value));

        var dataProvider = new WorldBuilder.Shared.Modules.Landscape.Services.LandscapeDataProvider(_mockRepo.Object);
        _mockDocManager.Setup(m => m.LandscapeDataProvider).Returns(dataProvider);
        _mockDocManager.Setup(m => m.LandscapeCacheService).Returns(new WorldBuilder.Shared.Modules.Landscape.Services.LandscapeCacheService());

        _mockDocManager.Setup(m => m.PersistDocumentAsync(It.IsAny<DocumentRental<TerrainPatchDocument>>(), It.IsAny<ITransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Unit>.Success(Unit.Value));
    }

    private (LandscapeDocument, DocumentRental<LandscapeDocument>) CreateMockTerrainRental() {
        var terrainDoc = new LandscapeDocument(_terrainDocId);

        // Bypass dats loading
        typeof(LandscapeDocument).GetField("_didLoadRegionData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(terrainDoc, true);
        typeof(LandscapeDocument).GetField("_didLoadLayers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(terrainDoc, true);

        // Inject dependencies manually since we are not using RentDocument in the test setup itself
        typeof(LandscapeDocument).GetField("_documentManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(terrainDoc, _mockDocManager.Object);
        typeof(LandscapeDocument).GetField("_landscapeDataProvider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(terrainDoc, _mockDocManager.Object.LandscapeDataProvider);

        var regionMock = new Mock<ITerrainInfo>();
        regionMock.Setup(r => r.MapWidthInLandblocks).Returns(128);
        regionMock.Setup(r => r.MapHeightInLandblocks).Returns(128);
        regionMock.Setup(r => r.GetLandblockId(It.IsAny<int>(), It.IsAny<int>())).Returns<int, int>((x, y) => (ushort)((x << 8) | y));
        terrainDoc.Region = regionMock.Object;

        var rental = new DocumentRental<LandscapeDocument>(terrainDoc, () => { });
        return (terrainDoc, rental);
    }

    private async Task SetupChunk(LandscapeDocument terrainDoc, ushort landblockId) {
        var x = (int)(landblockId >> 8);
        var y = (int)(landblockId & 0xFF);
        ushort chunkId = (ushort)(((uint)(x / 8) << 8) | (uint)(y / 8));

        if (!terrainDoc.LoadedChunks.ContainsKey(chunkId)) {
            var chunk = new LandscapeChunk(chunkId);
            chunk.EditsRental = new DocumentRental<TerrainPatchDocument>(new TerrainPatchDocument($"TerrainPatch_0_{chunkId}_0"), () => { });
            terrainDoc.LoadedChunks[chunkId] = chunk;
        }
    }

    [Fact]
    public async Task AddStaticObject_AppliesSuccessfully() {
        // Arrange
        var layerId = Guid.NewGuid().ToString();
        var (terrainDoc, terrainRental) = CreateMockTerrainRental();
        terrainDoc.AddLayer([], "Test Layer", false, layerId);

        ushort lbId = 0x0A0A;
        await SetupChunk(terrainDoc, lbId);

        var instanceId = ObjectId.FromDat(ObjectType.StaticObject, 0, lbId, 123);
        var obj = new StaticObject { InstanceId = instanceId, ModelId = 0x01000001, LayerId = layerId };
        var command = new AddStaticObjectCommand {
            TerrainDocumentId = _terrainDocId,
            LayerId = layerId,
            LandblockId = lbId,
            Object = obj
        };

        // Repo setup for Add
        _mockRepo.Setup(r => r.UpsertStaticObjectAsync(obj, It.IsAny<uint>(), lbId, obj.CellId, It.IsAny<ITransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Unit>.Success(Unit.Value));

        _mockDocManager.Setup(m => m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<ITransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));
        _mockDocManager.Setup(m => m.PersistDocumentAsync(terrainRental, It.IsAny<ITransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Unit>.Success(Unit.Value));

        // Act
        var result = await command.ApplyResultAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, default);

        // Assert
        Assert.True(result.IsSuccess);

        // Setup repo to return the object for merging test
        _mockRepo.Setup(r => r.GetStaticObjectsAsync(It.IsAny<ushort?>(), It.IsAny<uint?>(), It.IsAny<ITransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StaticObject> { obj });

        var merged = await terrainDoc.GetMergedLandblockAsync(lbId);
        Assert.Contains(merged.StaticObjects.Values, x => x.InstanceId == instanceId);
    }

    [Fact]
    public async Task DeleteStaticObject_AppliesSuccessfully() {
        // Arrange
        var layerId = Guid.NewGuid().ToString();
        var (terrainDoc, terrainRental) = CreateMockTerrainRental();
        terrainDoc.AddLayer([], "Test Layer", false, layerId);

        ushort lbId = 0x0A0A;
        await SetupChunk(terrainDoc, lbId);

        var instanceId = ObjectId.FromDat(ObjectType.StaticObject, 0, lbId, 123);
        var obj = new StaticObject { InstanceId = instanceId, ModelId = 0x01000001, LayerId = layerId };

        var command = new DeleteStaticObjectCommand {
            TerrainDocumentId = _terrainDocId,
            LayerId = layerId,
            LandblockId = lbId,
            InstanceId = instanceId,
            PreviousState = obj
        };

        // Repo setup
        _mockRepo.Setup(r => r.DeleteStaticObjectAsync(instanceId, It.IsAny<ITransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Unit>.Success(Unit.Value));

        _mockDocManager.Setup(m => m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<ITransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));
        _mockDocManager.Setup(m => m.PersistDocumentAsync(terrainRental, It.IsAny<ITransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Unit>.Success(Unit.Value));

        // Act
        var result = await command.ApplyResultAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, default);

        // Assert
        Assert.True(result.IsSuccess);

        // Mock repo returns empty list after delete
        _mockRepo.Setup(r => r.GetStaticObjectsAsync(It.IsAny<ushort?>(), It.IsAny<uint?>(), It.IsAny<ITransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StaticObject>());

        var merged = await terrainDoc.GetMergedLandblockAsync(lbId);
        Assert.DoesNotContain(merged.StaticObjects.Values, x => x.InstanceId == instanceId);
    }

    [Fact]
    public async Task UpdateStaticObject_AppliesSuccessfully() {
        // Arrange
        var layerId = Guid.NewGuid().ToString();
        var (terrainDoc, terrainRental) = CreateMockTerrainRental();
        terrainDoc.AddLayer([], "Test Layer", false, layerId);

        ushort oldLbId = 0x0A0A;
        ushort newLbId = 0x0B0B;
        await SetupChunk(terrainDoc, oldLbId);
        await SetupChunk(terrainDoc, newLbId);

        var instanceId = ObjectId.FromDat(ObjectType.StaticObject, 0, oldLbId, 123);
        var oldObj = new StaticObject { InstanceId = instanceId, ModelId = 0x01000001, Position = new System.Numerics.Vector3(1, 1, 1), Rotation = System.Numerics.Quaternion.Identity, LayerId = layerId };
        var newObj = new StaticObject { InstanceId = instanceId, ModelId = 0x01000001, Position = new System.Numerics.Vector3(2, 2, 2), Rotation = System.Numerics.Quaternion.Identity, LayerId = layerId };
        var command = new UpdateStaticObjectCommand {
            TerrainDocumentId = _terrainDocId,
            LayerId = layerId,
            OldLandblockId = oldLbId,
            NewLandblockId = newLbId,
            OldObject = oldObj,
            NewObject = newObj
        };

        // Repo setups
        _mockRepo.Setup(r => r.DeleteStaticObjectAsync(instanceId, It.IsAny<ITransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Unit>.Success(Unit.Value));
        _mockRepo.Setup(r => r.UpsertStaticObjectAsync(newObj, It.IsAny<uint>(), newLbId, newObj.CellId, It.IsAny<ITransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Unit>.Success(Unit.Value));

        _mockDocManager.Setup(m => m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<ITransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));
        _mockDocManager.Setup(m => m.PersistDocumentAsync(terrainRental, It.IsAny<ITransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Unit>.Success(Unit.Value));

        // Act
        var result = await command.ApplyResultAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, default);

        // Assert
        Assert.True(result.IsSuccess);

        // Mock repo results
        _mockRepo.Setup(r => r.GetStaticObjectsAsync(oldLbId, null, It.IsAny<ITransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StaticObject>());
        _mockRepo.Setup(r => r.GetStaticObjectsAsync(newLbId, null, It.IsAny<ITransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StaticObject> { newObj });

        var oldMerged = await terrainDoc.GetMergedLandblockAsync(oldLbId);
        var newMerged = await terrainDoc.GetMergedLandblockAsync(newLbId);
        Assert.DoesNotContain(oldMerged.StaticObjects.Values, x => x.InstanceId == instanceId);
        Assert.Contains(newMerged.StaticObjects.Values, x => x.InstanceId == instanceId && x.Position.X == 2);
    }

    [Fact]
    public async Task Undo_AddStaticObject_RemovesObject() {
        // Arrange
        var layerId = Guid.NewGuid().ToString();
        var (terrainDoc, terrainRental) = CreateMockTerrainRental();
        terrainDoc.AddLayer([], "Test Layer", false, layerId);
        ushort lbId = 0x0A0A;
        await SetupChunk(terrainDoc, lbId);

        var instanceId = ObjectId.FromDat(ObjectType.StaticObject, 0, lbId, 123);
        var obj = new StaticObject { InstanceId = instanceId, ModelId = 0x01000001, LayerId = layerId };
        var command = new AddStaticObjectCommand {
            TerrainDocumentId = _terrainDocId,
            LayerId = layerId,
            LandblockId = lbId,
            Object = obj
        };

        _mockDocManager.Setup(m => m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<ITransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));
        _mockDocManager.Setup(m => m.PersistDocumentAsync(terrainRental, It.IsAny<ITransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Unit>.Success(Unit.Value));

        // Act
        await command.ApplyResultAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, default);
        var inverse = (DeleteStaticObjectCommand)command.CreateInverse();

        // Mock repo for undo (Delete)
        _mockRepo.Setup(r => r.DeleteStaticObjectAsync(instanceId, It.IsAny<ITransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Unit>.Success(Unit.Value));

        var undoResult = await inverse.ApplyAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, default);

        // Assert
        Assert.True(undoResult.IsSuccess);

        // Mock repo returns empty list after undo
        _mockRepo.Setup(r => r.GetStaticObjectsAsync(lbId, null, It.IsAny<ITransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StaticObject>());

        var merged = await terrainDoc.GetMergedLandblockAsync(lbId);
        Assert.DoesNotContain(merged.StaticObjects.Values, x => x.InstanceId == instanceId);
    }
}
