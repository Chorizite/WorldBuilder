using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace WorldBuilder.Shared.Lib {
    /// <summary>
    /// Utility methods for handling DAT files.
    /// </summary>
    public static class DatUtils {
        /// <summary>
        /// Deletes DAT files within a directory and then deletes the directory if it's empty.
        /// </summary>
        /// <param name="directory">The directory containing the DAT files.</param>
        /// <param name="log">The logger to use for warnings.</param>
        public static void DeleteDatSet(string directory, ILogger log) {
            if (!Directory.Exists(directory)) return;

            try {
                // delete specific game dat files
                var datFiles = new[] { "client_portal.dat", "client_local_English.dat", "client_highres.dat", "client_cell.dat" };
                foreach (var datName in datFiles) {
                    var datPath = Path.Combine(directory, datName);
                    if (File.Exists(datPath)) {
                        try {
                            File.Delete(datPath);
                        }
                        catch (Exception ex) {
                            log.LogWarning(ex, "Failed to delete local DAT file: {datPath}", datPath);
                        }
                    }
                }

                // Delete iterative numbered cell dat region files (client_cell_1.dat, etc)
                for (int i = 1; i <= 100; i++) {
                    var numberedCellPath = Path.Combine(directory, $"client_cell_{i}.dat");
                    if (File.Exists(numberedCellPath)) {
                        try {
                            File.Delete(numberedCellPath);
                        }
                        catch (Exception ex) {
                            log.LogWarning(ex, "Failed to delete local numbered DAT file: {numberedCellPath}", numberedCellPath);
                        }
                    }
                }

                // Delete the directory only if it is empty after file deletions
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) {
                    Directory.Delete(directory);
                }
            }
            catch (Exception ex) {
                log.LogWarning(ex, "Failed to perform DAT set cleanup for directory: {directory}", directory);
            }
        }
    }
}
