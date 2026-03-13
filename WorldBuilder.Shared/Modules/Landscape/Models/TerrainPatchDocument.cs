using MemoryPack;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Models
{
    /// <summary>
    /// Represents a spatially-sharded document containing exclusively terrain data (heightmaps, etc.).
    /// ID Format: TerrainPatch_{regionId}_{chunkX}_{chunkY}
    /// </summary>
    [MemoryPackable]
    public partial class TerrainPatchDocument : BaseDocument
    {
        /// <summary>
        /// Terrain vertex edits per layer.
        /// Key: Layer ID
        /// Value: Array of 4225 vertices (65x65)
        /// </summary>
        [MemoryPackInclude]
        [MemoryPackOrder(10)]
        public Dictionary<string, TerrainEntry[]> LayerEdits { get; init; } = [];

        /// <summary>Initializes a new instance of the <see cref="TerrainPatchDocument"/> class.</summary>
        [MemoryPackConstructor]
        public TerrainPatchDocument() : base()
        {
        }

        /// <summary>Initializes a new instance of the <see cref="TerrainPatchDocument"/> class with a specific ID.</summary>
        /// <param name="id">The document ID.</param>
        public TerrainPatchDocument(string id) : base(id)
        {
            if (!id.StartsWith("TerrainPatch_"))
                throw new ArgumentException("TerrainPatchDocument Id must start with 'TerrainPatch_'", nameof(id));
        }

        /// <summary>Constructs a document ID from region and chunk coordinates.</summary>
        public static string GetId(uint regionId, uint chunkX, uint chunkY) =>
            $"TerrainPatch_{regionId}_{chunkX}_{chunkY}";

        /// <inheritdoc/>
        public override Task InitializeForUpdatingAsync(IDatReaderWriter dats, IDocumentManager documentManager, ITransaction? tx, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public override Task InitializeForEditingAsync(IDatReaderWriter dats, IDocumentManager documentManager, ITransaction? tx, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        protected override Task<bool> SaveToDatsInternal(IDatReaderWriter datwriter, int portalIteration = 0, int cellIteration = 0, IProgress<float>? progress = null)
        {
            return Task.FromResult(true);
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            LayerEdits.Clear();
        }
    }
}
