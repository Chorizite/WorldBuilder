using MemoryPack;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Models;

[MemoryPackable]
[MemoryPackUnion(0, typeof(LandscapeDocument))]
[MemoryPackUnion(1, typeof(LandscapeLayerDocument))]
public abstract partial class BaseDocument : IDisposable {
    [MemoryPackOrder(0)]
    public string Id { get; init; }

    [MemoryPackOrder(1)]
    public ulong Version { get; set; } = 0;

    [MemoryPackConstructor]
    public BaseDocument() {
        Id = $"{GetType().Name}_{Guid.NewGuid()}";
    }

    protected BaseDocument(string id) {
        Id = id ?? throw new ArgumentNullException(nameof(id));
    }

    public byte[] Serialize() => MemoryPackSerializer.Serialize<BaseDocument>(this);

    public static T? Deserialize<T>(byte[] blob) where T : BaseDocument {
        return MemoryPackSerializer.Deserialize<BaseDocument>(blob) as T;
    }

    public abstract Task InitializeForUpdatingAsync(IDatReaderWriter dats, IDocumentManager documentManager, CancellationToken ct);

    public abstract Task InitializeForEditingAsync(IDatReaderWriter dats, IDocumentManager documentManager, CancellationToken ct);
    public abstract void Dispose();
}