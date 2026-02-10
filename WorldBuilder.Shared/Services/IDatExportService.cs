namespace WorldBuilder.Shared.Services {
    /// <summary>
    /// Progress information for DAT export.
    /// </summary>
    /// <param name="Message">The current status message.</param>
    /// <param name="Percent">The completion percentage (0.0 to 1.0).</param>
    public record DatExportProgress(string Message, float Percent);

    /// <summary>
    /// Service for exporting modified terrain data to DAT files.
    /// </summary>
    public interface IDatExportService {
        /// <summary>
        /// Exports modified terrain data from the current document to DAT files in the specified directory.
        /// </summary>
        /// <param name="exportDirectory">The directory to export to.</param>
        /// <param name="portalIteration">The portal iteration to use.</param>
        /// <param name="overwrite">Whether to overwrite existing files.</param>
        /// <param name="progress">The progress reporter.</param>
        /// <returns>A task representing the asynchronous operation, returning true if successful.</returns>
        Task<bool> ExportDatsAsync(string exportDirectory, int portalIteration, bool overwrite = true, IProgress<DatExportProgress>? progress = null);
    }
}
