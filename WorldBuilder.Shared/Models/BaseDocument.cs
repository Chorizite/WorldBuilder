using MemoryPack;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Models;

/// <summary>
/// Base class for all documents in the system.
/// Documents are serializable entities that represent a piece of data that can be versioned and persisted.
/// </summary>
[MemoryPackable]
[MemoryPackUnion(0, typeof(LandscapeDocument))]
[MemoryPackUnion(1, typeof(LandscapeLayerDocument))]
public abstract partial class BaseDocument : IDisposable {
    /// <summary>The unique identifier for the document.</summary>
    [MemoryPackOrder(0)]
    public string Id { get; init; }

    /// <summary>The current version of the document.</summary>
    [MemoryPackOrder(1)]
    public ulong Version { get; set; } = 0;

    /// <summary>Initializes a new instance of the <see cref="BaseDocument"/> class with a random ID.</summary>
    [MemoryPackConstructor]
    public BaseDocument() {
        Id = $"{GetType().Name}_{Guid.NewGuid()}";
    }

    /// <summary>Initializes a new instance of the <see cref="BaseDocument"/> class with a specific ID.</summary>
    /// <param name="id">The fixed ID for the document.</param>
    protected BaseDocument(string id) {
        Id = id ?? throw new ArgumentNullException(nameof(id));
    }

    /// <summary>Serializes the document to a byte array.</summary>
    /// <returns>A byte array representing the serialized document.</returns>
    public byte[] Serialize() => MemoryPackSerializer.Serialize<BaseDocument>(this);

    /// <summary>Deserializes a document from a byte array.</summary>
    /// <typeparam name="T">The type of document to deserialize to.</typeparam>
    /// <param name="blob">The byte array to deserialize.</param>
    /// <returns>The deserialized document, or null if deserialization failed.</returns>
    public static T? Deserialize<T>(byte[] blob) where T : BaseDocument {
        return MemoryPackSerializer.Deserialize<BaseDocument>(blob) as T;
    }

    /// <summary>Initializes the document for updating asynchronously.</summary>
    /// <param name="dats">The DAT reader/writer.</param>
    /// <param name="documentManager">The document manager.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public abstract Task InitializeForUpdatingAsync(IDatReaderWriter dats, IDocumentManager documentManager, CancellationToken ct);

    /// <summary>Initializes the document for editing asynchronously.</summary>
    /// <param name="dats">The DAT reader/writer.</param>
    /// <param name="documentManager">The document manager.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public abstract Task InitializeForEditingAsync(IDatReaderWriter dats, IDocumentManager documentManager, CancellationToken ct);

    /// <summary>Disposes of the document resources.</summary>
    public abstract void Dispose();
}