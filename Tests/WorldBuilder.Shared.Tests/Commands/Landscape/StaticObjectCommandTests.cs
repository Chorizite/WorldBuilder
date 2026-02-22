using Moq;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Tests.Commands.Landscape;

public class StaticObjectCommandTests {
    private readonly Mock<IDocumentManager> _mockDocManager;
    private readonly Mock<IDatReaderWriter> _mockDats;
    private readonly Mock<ITransaction> _mockTx;
    private readonly string _terrainDocId = "LandscapeDocument_1";

    public StaticObjectCommandTests() {
        _mockDocManager = new Mock<IDocumentManager>();
        _mockDats = new Mock<IDatReaderWriter>();
        _mockTx = new Mock<ITransaction>();

        _mockDocManager.Setup(m => m.PersistDocumentAsync(It.IsAny<DocumentRental<LandscapeChunkDocument>>(), It.IsAny<ITransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Unit>.Success(Unit.Value));
    }

    private (LandscapeDocument, DocumentRental<LandscapeDocument>) CreateMockTerrainRental() {
        var terrainDoc = new LandscapeDocument(_terrainDocId);

        // Bypass dats loading
        typeof(LandscapeDocument).GetField("_didLoadRegionData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(terrainDoc, true);
        typeof(LandscapeDocument).GetField("_didLoadLayers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(terrainDoc, true);

        var regionMock = new Mock<ITerrainInfo>();
        regionMock.Setup(r => r.MapWidthInLandblocks).Returns(128);
        regionMock.Setup(r => r.MapHeightInLandblocks).Returns(128);
        regionMock.Setup(r => r.GetLandblockId(It.IsAny<int>(), It.IsAny<int>())).Returns<int, int>((x, y) => (ushort)((x << 8) | y));
        terrainDoc.Region = regionMock.Object;

        var rental = new DocumentRental<LandscapeDocument>(terrainDoc, () => { });
        return (terrainDoc, rental);
    }

    private void SetupChunk(LandscapeDocument terrainDoc, uint landblockId) {
        var x = (int)(landblockId >> 24);
        var y = (int)((landblockId >> 16) & 0xFF);
        ushort chunkId = (ushort)(( (uint)(x / 8) << 8) | (uint)(y / 8));
        
        if (!terrainDoc.LoadedChunks.ContainsKey(chunkId)) {
            var chunk = new LandscapeChunk(chunkId);
            chunk.EditsRental = new DocumentRental<LandscapeChunkDocument>(new LandscapeChunkDocument($"LandscapeChunkDocument_{chunkId}"), () => { });
            terrainDoc.LoadedChunks[chunkId] = chunk;
        }
    }

    [Fact]
    public async Task AddStaticObject_AppliesSuccessfully() {
        // Arrange
        var layerId = Guid.NewGuid().ToString();
        var (terrainDoc, terrainRental) = CreateMockTerrainRental();
        terrainDoc.AddLayer([], "Test Layer", false, layerId);
        
        uint lbId = (10u << 24) | (10u << 16) | 0xFFFE;
        SetupChunk(terrainDoc, lbId);

        var obj = new StaticObject { InstanceId = 123, SetupId = 0x01000001 };
        var command = new AddStaticObjectCommand {
            TerrainDocumentId = _terrainDocId,
            LayerId = layerId,
            LandblockId = lbId,
            Object = obj
        };

        _mockDocManager.Setup(m => m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));
        _mockDocManager.Setup(m => m.PersistDocumentAsync(terrainRental, _mockTx.Object, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Unit>.Success(Unit.Value));

        // Act
        var result = await command.ApplyResultAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, default);

        // Assert
        Assert.True(result.IsSuccess);
        var merged = terrainDoc.GetMergedLandblock(lbId);
        Assert.Contains(merged.StaticObjects, x => x.InstanceId == 123);
    }

    [Fact]
    public async Task DeleteStaticObject_AppliesSuccessfully() {
        // Arrange
        var layerId = Guid.NewGuid().ToString();
        var (terrainDoc, terrainRental) = CreateMockTerrainRental();
        terrainDoc.AddLayer([], "Test Layer", false, layerId);
        
        uint lbId = (10u << 24) | (10u << 16) | 0xFFFE;
        SetupChunk(terrainDoc, lbId);

        var obj = new StaticObject { InstanceId = 123, SetupId = 0x01000001 };
        terrainDoc.AddStaticObject(layerId, lbId, obj);

        var command = new DeleteStaticObjectCommand {
            TerrainDocumentId = _terrainDocId,
            LayerId = layerId,
            LandblockId = lbId,
            InstanceId = 123,
            PreviousState = obj
        };

        _mockDocManager.Setup(m => m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));
        _mockDocManager.Setup(m => m.PersistDocumentAsync(terrainRental, _mockTx.Object, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Unit>.Success(Unit.Value));

        // Act
        var result = await command.ApplyResultAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, default);

        // Assert
        Assert.True(result.IsSuccess);
        var merged = terrainDoc.GetMergedLandblock(lbId);
        Assert.DoesNotContain(merged.StaticObjects, x => x.InstanceId == 123);
    }

    [Fact]
    public async Task UpdateStaticObject_AppliesSuccessfully() {
        // Arrange
        var layerId = Guid.NewGuid().ToString();
        var (terrainDoc, terrainRental) = CreateMockTerrainRental();
        terrainDoc.AddLayer([], "Test Layer", false, layerId);
        
        uint oldLbId = (10u << 24) | (10u << 16) | 0xFFFE;
        uint newLbId = (11u << 24) | (11u << 16) | 0xFFFE;
        SetupChunk(terrainDoc, oldLbId);
        SetupChunk(terrainDoc, newLbId);

        var oldObj = new StaticObject { InstanceId = 123, SetupId = 0x01000001, Position = [1, 1, 1, 0, 0, 0, 1] };
        terrainDoc.AddStaticObject(layerId, oldLbId, oldObj);

        var newObj = new StaticObject { InstanceId = 123, SetupId = 0x01000001, Position = [2, 2, 2, 0, 0, 0, 1] };
        var command = new UpdateStaticObjectCommand {
            TerrainDocumentId = _terrainDocId,
            LayerId = layerId,
            OldLandblockId = oldLbId,
            NewLandblockId = newLbId,
            OldObject = oldObj,
            NewObject = newObj
        };

        _mockDocManager.Setup(m => m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));
        _mockDocManager.Setup(m => m.PersistDocumentAsync(terrainRental, _mockTx.Object, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Unit>.Success(Unit.Value));

        // Act
        var result = await command.ApplyResultAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, default);

        // Assert
        Assert.True(result.IsSuccess);
        var oldMerged = terrainDoc.GetMergedLandblock(oldLbId);
        var newMerged = terrainDoc.GetMergedLandblock(newLbId);
        Assert.DoesNotContain(oldMerged.StaticObjects, x => x.InstanceId == 123);
        Assert.Contains(newMerged.StaticObjects, x => x.InstanceId == 123 && x.Position[0] == 2);
    }

    [Fact]
    public async Task Undo_AddStaticObject_RemovesObject() {
        // Arrange
        var layerId = Guid.NewGuid().ToString();
        var (terrainDoc, terrainRental) = CreateMockTerrainRental();
        terrainDoc.AddLayer([], "Test Layer", false, layerId);
        uint lbId = (10u << 24) | (10u << 16) | 0xFFFE;
        SetupChunk(terrainDoc, lbId);

        var obj = new StaticObject { InstanceId = 123, SetupId = 0x01000001 };
        var command = new AddStaticObjectCommand {
            TerrainDocumentId = _terrainDocId,
            LayerId = layerId,
            LandblockId = lbId,
            Object = obj
        };

        _mockDocManager.Setup(m => m.RentDocumentAsync<LandscapeDocument>(_terrainDocId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DocumentRental<LandscapeDocument>>.Success(terrainRental));
        _mockDocManager.Setup(m => m.PersistDocumentAsync(terrainRental, _mockTx.Object, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Unit>.Success(Unit.Value));

        // Act
        await command.ApplyResultAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, default);
        var inverse = command.CreateInverse();
        var undoResult = await inverse.ApplyAsync(_mockDocManager.Object, _mockDats.Object, _mockTx.Object, default);

        // Assert
        Assert.True(undoResult.IsSuccess);
        var merged = terrainDoc.GetMergedLandblock(lbId);
        Assert.DoesNotContain(merged.StaticObjects, x => x.InstanceId == 123);
    }
}
