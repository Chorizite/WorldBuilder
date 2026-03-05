using DatReaderWriter.Lib;
using MemoryPack;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Modules.Landscape.Commands {
    /// <summary>
    /// Command to create a new landscape document for a specific region.
    /// </summary>
    [MemoryPackable]
    public partial class CreateLandscapeDocumentCommand : BaseCommand<DocumentRental<LandscapeDocument>?> {
        /// <summary>The region ID for the landscape document.</summary>
        [MemoryPackOrder(10)]
        public uint RegionId { get; set; }

        /// <summary>Initializes a new instance of the <see cref="CreateLandscapeDocumentCommand"/> class.</summary>
        [MemoryPackConstructor]
        public CreateLandscapeDocumentCommand() { }

        /// <summary>Initializes a new instance of the <see cref="CreateLandscapeDocumentCommand"/> class with a region ID.</summary>
        /// <param name="regionId">The region ID.</param>
        public CreateLandscapeDocumentCommand(uint regionId) {
            RegionId = regionId;
        }

        public override BaseCommand CreateInverse() {
            throw new NotImplementedException();
        }

        public override async Task<Result<bool>> ApplyAsync(IDocumentManager documentManager, IDatReaderWriter dats, ITransaction tx, CancellationToken ct) {
            var result = await ApplyResultAsync(documentManager, dats, tx, ct);
            return result.IsSuccess ? Result<bool>.Success(result.Value != null) : Result<bool>.Failure(result.Error);
        }

        public override async Task<Result<DocumentRental<LandscapeDocument>?>> ApplyResultAsync(IDocumentManager documentManager, IDatReaderWriter dats, ITransaction tx, CancellationToken ct) {
            try {
                var terrainDoc = new LandscapeDocument(LandscapeDocument.GetIdFromRegion(RegionId));
                var createResult = await documentManager.CreateDocumentAsync(terrainDoc, tx, ct);

                if (createResult.IsFailure) {
                    return Result<DocumentRental<LandscapeDocument>?>.Failure(createResult.Error);
                }

                var terrainRental = createResult.Value;
                await terrainRental.Document.InitializeForUpdatingAsync(dats, documentManager, ct);

                // Create base layer doc
                var createLayerCommand = new CreateLandscapeLayerCommand(terrainDoc.Id, [], "Base Layer", true);
                var layerResult = await documentManager.ApplyLocalEventAsync(createLayerCommand, tx, ct);

                if (layerResult.IsFailure) {
                    return Result<DocumentRental<LandscapeDocument>?>.Failure(layerResult.Error);
                }

                terrainRental.Document.Version++;
                var persistResult = await documentManager.PersistDocumentAsync(terrainRental, tx, ct);

                if (persistResult.IsFailure) {
                    return Result<DocumentRental<LandscapeDocument>?>.Failure(persistResult.Error);
                }

                return Result<DocumentRental<LandscapeDocument>?>.Success(terrainRental);
            }
            catch (Exception ex) {
                return Result<DocumentRental<LandscapeDocument>?>.Failure(Error.Failure($"Error creating landscape document: {ex.Message}"));
            }
        }
    }
}