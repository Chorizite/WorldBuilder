using Microsoft.Extensions.Logging.Abstractions;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Repositories;
using WorldBuilder.Shared.Services;
using WorldBuilder.Shared.Tests.Mocks;

namespace WorldBuilder.Shared.Tests.Services;

public class SyncServiceTests : IAsyncLifetime {
    private TestDatabase _db = null!;
    private SQLiteProjectRepository _repo = null!;
    private MockSyncClient _client = null!;
    private MockDatReaderWriter _dats = null!;
    private DocumentManager _docMgr = null!;
    private SyncService _sync = null!;
    private readonly string _userId = Guid.NewGuid().ToString();

    public async Task InitializeAsync() {
        _db = new TestDatabase();
        _repo = new SQLiteProjectRepository(_db.ConnectionString);
        _client = new MockSyncClient();
        _dats = new MockDatReaderWriter();

        await _repo.InitializeDatabaseAsync(default);
        _docMgr = new DocumentManager(_repo, _dats, NullLogger<DocumentManager>.Instance);
        await _docMgr.InitializeAsync(default);
        _sync = new SyncService(_docMgr, _client, _repo, _dats, _userId);
    }

    public Task DisposeAsync() {
        _repo.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task StartAsync_ConnectsToServer() {
        // Arrange & Act
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await _sync.StartAsync(cts.Token);

        // Assert
        Assert.True(_client.IsConnected);
    }

    [Fact]
    public async Task StopAsync_DisconnectsFromServer() {
        // Arrange
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await _sync.StartAsync(cts.Token);

        // Act
        await _sync.StopAsync();

        // Assert
        Assert.False(_client.IsConnected);
    }

    [Fact]
    public async Task ApplyLocalEvent_SendsToServerWhenOnline() {
        // Arrange
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _sync.StartAsync(cts.Token);

        // Create a landscape document - uses uint regionId
        var createDocCmd = new CreateLandscapeDocumentCommand(0x00010001);
        await _sync.ApplyLocalEventAsync(createDocCmd, cts.Token);

        // Assert - event should be in sent queue
        Assert.True(_client.SentEvents.TryDequeue(out var sent));
        Assert.Equal(createDocCmd.Id, sent.Id);
        Assert.True(sent.ServerTimestamp.HasValue); // Server should assign timestamp
    }

    [Fact]
    public async Task GetEventsSince_ReturnsMissedEvents() {
        // Arrange - simulate events stored on "server"
        var evt1 = new CreateLandscapeDocumentCommand(0x00010001) { UserId = "other-user", ServerTimestamp = 101 };
        var evt2 = new CreateLandscapeDocumentCommand(0x00010002) { UserId = "other-user", ServerTimestamp = 102 };
        _client.StoredEvents.Add(evt1);
        _client.StoredEvents.Add(evt2);

        // Act
        var events = await _client.GetEventsSinceAsync(100, default);

        // Assert
        Assert.Equal(2, events.Count);
        Assert.Equal(101ul, events[0].ServerTimestamp);
        Assert.Equal(102ul, events[1].ServerTimestamp);
    }
}