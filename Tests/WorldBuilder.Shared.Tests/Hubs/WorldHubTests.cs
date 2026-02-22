using WorldBuilder.Server.Hubs;
using WorldBuilder.Server.Services;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Modules.Landscape.Commands;

namespace WorldBuilder.Shared.Tests.Hubs;

public class WorldHubTests {
    [Fact]
    public async Task GetServerTime_ReturnsMonotonicTimestamp() {
        // Arrange
        var hub = new WorldHub(new InMemoryWorldEventStore());

        // Act
        var time1 = await hub.GetServerTime();
        var time2 = await hub.GetServerTime();
        var time3 = await hub.GetServerTime();

        // Assert - timestamps should be monotonically increasing or equal
        Assert.True(time2 >= time1);
        Assert.True(time3 >= time2);
    }

    [Fact]
    public async Task GetEventsSince_ReturnsEmptyForNewServer() {
        // Arrange
        var hub = new WorldHub(new InMemoryWorldEventStore());

        // Act
        var events = await hub.GetEventsSince(0);

        // Assert
        Assert.Empty(events);
    }

    [Fact]
    public async Task GetEventsSince_ReturnsCorrectType() {
        // This test would require mocking the hub context for full testing
        // For now, just verify the method returns an array
        var hub = new WorldHub(new InMemoryWorldEventStore());

        var events = await hub.GetEventsSince(50);

        Assert.NotNull(events);
        Assert.IsType<byte[][]>(events);
    }
}