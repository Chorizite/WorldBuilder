using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Modules.Landscape {
    /// <summary>
    /// Interface for the landscape module.
    /// </summary>
    public interface ILandscapeModule {
        /// <summary>
        /// Gets or creates a landscape document for the specified region ID asynchronously.
        /// </summary>
        /// <param name="regionId">The region ID.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task containing a rental for the landscape document.</returns>
        Task<DocumentRental<LandscapeDocument>> GetOrCreateTerrainDocumentAsync(uint regionId, CancellationToken ct);
    }
}