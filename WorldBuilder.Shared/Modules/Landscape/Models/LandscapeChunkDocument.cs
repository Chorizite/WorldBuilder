using MemoryPack;
using System.Collections.Generic;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Models
{
    /// <summary>
    /// Represents a spatially-sharded chunk of landscape edits within a region.
    /// ID Format: LandscapeChunkDocument_{regionId}_{chunkX}_{chunkY}
    /// </summary>
    [MemoryPackable]
    public partial class LandscapeChunkDocument : BaseDocument
    {
        /// <summary>
        /// Edits for each layer within this spatial chunk.
        /// Key: Layer ID
        /// </summary>
        [MemoryPackInclude]
        [MemoryPackOrder(10)]
        public Dictionary<string, LandscapeChunkEdits> LayerEdits { get; init; } = [];

        /// <summary>Initializes a new instance of the <see cref="LandscapeChunkDocument"/> class.</summary>
        [MemoryPackConstructor]
        public LandscapeChunkDocument() : base()
        {
        }

        /// <summary>Initializes a new instance of the <see cref="LandscapeChunkDocument"/> class with a specific ID.</summary>
        /// <param name="id">The document ID.</param>
        public LandscapeChunkDocument(string id) : base(id)
        {
            if (!id.StartsWith($"{nameof(LandscapeChunkDocument)}_"))
                throw new ArgumentException($"LandscapeChunkDocument Id must start with '{nameof(LandscapeChunkDocument)}_'", nameof(id));
        }

        /// <summary>Constructs a document ID from region and chunk coordinates.</summary>
        public static string GetId(uint regionId, uint chunkX, uint chunkY) =>
            $"{nameof(LandscapeChunkDocument)}_{regionId}_{chunkX}_{chunkY}";

        /// <inheritdoc/>
        public override Task InitializeForUpdatingAsync(IDatReaderWriter dats, IDocumentManager documentManager, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public override Task InitializeForEditingAsync(IDatReaderWriter dats, IDocumentManager documentManager, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        protected override Task<bool> SaveToDatsInternal(IDatReaderWriter datwriter, int iteration = 0, IProgress<float>? progress = null)
        {
            // These documents don't save to DATs directly yet, they are managed by DocumentManager
            return Task.FromResult(true);
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            LayerEdits.Clear();
        }
    }
}
