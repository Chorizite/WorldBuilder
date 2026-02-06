using MemoryPack;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Models;

[MemoryPackable]
public partial class LandscapeLayerDocument : BaseDocument {
    [MemoryPackInclude]
    [MemoryPackOrder(10)]
    public Dictionary<uint, TerrainEntry> Terrain { get; init; } = [];

    public static string CreateId() => $"{nameof(LandscapeLayerDocument)}_{Guid.NewGuid()}";

    [MemoryPackConstructor]
    public LandscapeLayerDocument() : base() { }

    public LandscapeLayerDocument(string id) : base(id) {
        if (!id.StartsWith($"{nameof(LandscapeLayerDocument)}_"))
            throw new ArgumentException($"TerrainLayerDocument Id must start with '{nameof(LandscapeLayerDocument)}_'", nameof(id));
    }

    public override Task InitializeForUpdatingAsync(IDatReaderWriter dats, IDocumentManager documentManager, CancellationToken ct) {
        return Task.CompletedTask;
    }

    public override async Task InitializeForEditingAsync(IDatReaderWriter dats, IDocumentManager documentManager, CancellationToken ct) {
        await InitializeForUpdatingAsync(dats, documentManager, ct);
    }

    public override void Dispose() {

    }
}