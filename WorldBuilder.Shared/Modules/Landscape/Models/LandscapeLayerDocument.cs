using MemoryPack;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Models;

/// <summary>
/// Represents a document containing the data for a specific landscape layer.
/// </summary>
[MemoryPackable]
public partial class LandscapeLayerDocument : BaseDocument {
    /// <summary>The terrain data stored in this layer, mapping vertex index to terrain entry.</summary>
    [MemoryPackInclude]
    [MemoryPackOrder(10)]
    public Dictionary<uint, TerrainEntry> Terrain { get; init; } = [];

    /// <summary>Generates a unique ID for a new landscape layer document.</summary>
    /// <returns>A formatted document ID.</returns>
    public static string CreateId() => $"{nameof(LandscapeLayerDocument)}_{Guid.NewGuid()}";

    /// <summary>Initializes a new instance of the <see cref="LandscapeLayerDocument"/> class.</summary>
    [MemoryPackConstructor]
    public LandscapeLayerDocument() : base() { }

    /// <summary>Initializes a new instance of the <see cref="LandscapeLayerDocument"/> class with a specific ID.</summary>
    /// <param name="id">The document ID.</param>
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