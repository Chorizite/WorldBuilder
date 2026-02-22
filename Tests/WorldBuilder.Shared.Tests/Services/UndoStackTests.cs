// WorldBuilder.Shared.Tests/Services/UndoStackTests.cs
using Microsoft.Data.Sqlite;
using Moq;
using System.Threading.Channels;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Repositories;
using WorldBuilder.Shared.Services;
using WorldBuilder.Shared.Tests.Extensions;
using WorldBuilder.Shared.Tests.Mocks;

namespace WorldBuilder.Shared.Tests.Services;

/*
public class UndoStackTests : IAsyncLifetime {
    private readonly TestDatabase _db;
    private readonly SqliteConnection _conn;
    private readonly SQLiteProjectRepository _repo;
    private readonly Mock<IDocumentManager> _docManagerMock;
    private readonly DocumentManager _docMgr;
    private readonly UndoStack _undoStack;
    private readonly string _userId = Guid.NewGuid().ToString();

    public UndoStackTests() {
        _db = new TestDatabase();
        _conn = new SqliteConnection(_db.ConnectionString);
        _conn.Open();

        _repo = new SQLiteProjectRepository(_conn);
        _docManagerMock = new Mock<IDocumentManager>();
        _docMgr = new DocumentManager(_repo);
        _undoStack = new UndoStack(_docMgr);
    }

    public async Task InitializeAsync() => await _repo.InitializeDatabaseAsync(default);
    public async Task DisposeAsync() {
        await _conn.CloseAsync();
        await _conn.DisposeAsync();
    }

    [Fact]
    public async Task PushAndUndo_MultiDocument_AppliesInverseEvents() {
        var terrainDocId = new TerrainDocument().Id;
        var layerDocId = new TerrainLayerDocument().Id;
        var evt1 = new TerrainUpdateEvent {
            DocumentId = terrainDocId,
            UserId = _userId,
            ClientTimestamp = 1,
            PreviousState = new() { { 0x1234, new TerrainEntry { Height = 1, Type = new(1, 2), Road = false } } },
            Changes = new() { { 0x1234, new TerrainEntry { Height = 10, Road = true } } }
        };
        var evt2 = new TerrainUpdateEvent {
            DocumentId = layerDocId,
            UserId = _userId,
            ClientTimestamp = 2,
            PreviousState = new() { { 0x5678, new TerrainEntry { Height = 1, Type = new(1, 2), Road = false } } },
            Changes = new() { { 0x5678, new TerrainEntry { Type = new(3, 4) } } }
        };

        await _docMgr.ApplyEventAsync(evt1, default);
        await _docMgr.ApplyEventAsync(evt2, default);
        _undoStack.Push(new[] { evt1, evt2 });
        var inverses = await _undoStack.UndoAsync(default);

        Assert.NotNull(inverses);
        Assert.Equal(2, inverses.Count);
        var terrainDoc = await _docMgr.GetDocumentAsync<TerrainDocument>(terrainDocId, default);
        var layerDoc = await _docMgr.GetDocumentAsync<TerrainLayerDocument>(layerDocId, default);
        Assert.NotNull(terrainDoc);
        Assert.NotNull(layerDoc);

        Assert.Equal(evt1.PreviousState[0x1234].Height, terrainDoc!.Terrain[0x1234].Height);
        Assert.Equal(evt1.PreviousState[0x1234].Road, terrainDoc!.Terrain[0x1234].Road);

        Assert.Equal(evt2.PreviousState[0x5678].Type.Value.Texture, layerDoc!.Terrain[0x5678].Type.Value.Texture);
        Assert.Equal(evt2.PreviousState[0x5678].Type.Value.Scenery, layerDoc!.Terrain[0x5678].Type.Value.Scenery);
    }

    [Fact]
    public async Task Redo_MultiDocument_ReappliesOriginalEvents() {
        var terrainDocId = new TerrainDocument().Id;
        var layerDocId = new TerrainLayerDocument().Id;
        var evt1 = new TerrainUpdateEvent {
            DocumentId = terrainDocId,
            UserId = _userId,
            ClientTimestamp = 1,
            Changes = new() { { 0x1234, new TerrainEntry { Height = 10, Type = new(1, 2), Road = true } } }
        };
        var evt2 = new TerrainUpdateEvent {
            DocumentId = layerDocId,
            UserId = _userId,
            ClientTimestamp = 2,
            Changes = new() { { 0x5678, new TerrainEntry { Height = 20, Type = new(3, 4), Road = false } } }
        };

        await _docMgr.ApplyEventAsync(evt1, default);
        await _docMgr.ApplyEventAsync(evt2, default);
        _undoStack.Push(new[] { evt1, evt2 });
        await _undoStack.UndoAsync(default);
        var redone = await _undoStack.RedoAsync(default);

        Assert.NotNull(redone);
        Assert.Equal(2, redone.Count);
        var terrainDoc = await _docMgr.GetDocumentAsync<TerrainDocument>(terrainDocId, default);
        var layerDoc = await _docMgr.GetDocumentAsync<TerrainLayerDocument>(layerDocId, default);
        Assert.NotNull(terrainDoc);
        Assert.NotNull(layerDoc);
        Assert.True(terrainDoc!.Terrain.ContainsKey(0x1234));
        Assert.Equal(10, terrainDoc.Terrain[0x1234].Height.Value);
        Assert.Equal(new TextureScenery(1, 2), terrainDoc.Terrain[0x1234].Type);
        Assert.True(terrainDoc.Terrain[0x1234].Road);
        Assert.True(layerDoc!.Terrain.ContainsKey(0x5678));
        Assert.Equal(20, layerDoc.Terrain[0x5678].Height.Value);
        Assert.Equal(new TextureScenery(3, 4), layerDoc.Terrain[0x5678].Type);
        Assert.False(layerDoc.Terrain[0x5678].Road);
    }

    [Fact]
    public async Task Undo_WithPreviousState_RestoresCorrectly() {
        var terrainDocId = new TerrainDocument().Id;
        var initialEvt = new TerrainUpdateEvent {
            DocumentId = terrainDocId,
            UserId = _userId,
            ClientTimestamp = 1,
            PreviousState = new() { { 0x1234, new TerrainEntry { Height = 0, Type = new(1, 1), Road = false } } },
            Changes = new() { { 0x1234, new TerrainEntry { Height = 5, Type = new(1, 1), Road = false } } }
        };
        await _docMgr.ApplyEventAsync(initialEvt, default);
        _undoStack.Push(new[] { initialEvt });

        var updateEvt = new TerrainUpdateEvent {
            DocumentId = terrainDocId,
            UserId = _userId,
            ClientTimestamp = 2,
            Changes = new() { { 0x1234, new TerrainEntry { Height = 10, Type = new(2, 2), Road = true } } },
            PreviousState = initialEvt.Changes.ToDictionary()
        };
        await _docMgr.ApplyEventAsync(updateEvt, default);
        _undoStack.Push(new[] { updateEvt });
        var inverses = await _undoStack.UndoAsync(default);

        Assert.NotNull(inverses);
        Assert.Single(inverses);
        var terrainDoc = await _docMgr.GetDocumentAsync<TerrainDocument>(terrainDocId, default);
        Assert.NotNull(terrainDoc);
        Assert.True(terrainDoc!.Terrain.ContainsKey(0x1234));
        Assert.Equal(5, terrainDoc.Terrain[0x1234].Height.Value);
        Assert.Equal(new TextureScenery(1, 1), terrainDoc.Terrain[0x1234].Type);
        Assert.False(terrainDoc.Terrain[0x1234].Road);
    }
}*/