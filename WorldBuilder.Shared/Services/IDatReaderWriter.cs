using DatReaderWriter;
using DatReaderWriter.Enums;
using DatReaderWriter.Lib.IO;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace WorldBuilder.Shared.Services {
    /// <summary>
    /// Interface for the dat reader/writer
    /// </summary>
    public interface IDatReaderWriter : IDisposable {
        /// <summary>
        /// Gets the source directory of the DAT files.
        /// </summary>
        string SourceDirectory { get; }

        /// <summary>
        /// Tries to get the raw bytes of a file from a specific region database.
        /// </summary>
        bool TryGetFileBytes(uint regionId, uint fileId, ref byte[] bytes, out int bytesRead);

        /// <summary>
        /// The portal database
        /// </summary>
        IDatDatabase Portal { get; }

        /// <summary>
        /// The cell region databases. Each key is a cell region ID
        /// </summary>
        ReadOnlyDictionary<uint, IDatDatabase> CellRegions { get; }

        /// <summary>
        /// The high res database
        /// </summary>
        IDatDatabase HighRes { get; }

        /// <summary>
        /// The language database
        /// </summary>
        IDatDatabase Language { get; }

        /// <summary>
        /// A mapping of region ids to region dat file entry ids. key: region id, value: region dat file entry
        /// </summary>
        ReadOnlyDictionary<uint, uint> RegionFileMap { get; }

        /// <summary>
        /// Gets the current portal iteration.
        /// </summary>
        int PortalIteration { get; }

        /// <summary>Attempts to save a database object to the appropriate DAT.</summary>
        /// <typeparam name="T">The type of database object.</typeparam>
        /// <param name="obj">The object to save.</param>
        /// <param name="iteration">The iteration to save as.</param>
        /// <returns>True if the object was saved; otherwise, false.</returns>
        bool TrySave<T>(T obj, int iteration = 0) where T : IDBObj;

        /// <summary>Attempts to save a database object to the appropriate DAT for a specific region.</summary>
        /// <typeparam name="T">The type of database object.</typeparam>
        /// <param name="regionId">The region ID.</param>
        /// <param name="obj">The object to save.</param>
        /// <param name="iteration">The iteration to save as.</param>
        /// <returns>True if the object was saved; otherwise, false.</returns>
        bool TrySave<T>(uint regionId, T obj, int iteration = 0) where T : IDBObj;

        public DBObjType TypeFromId(uint id);
    }

    /// <summary>
    /// Interface for a dat database, providing methods to retrieve files and objects.
    /// </summary>
    public interface IDatDatabase : IDisposable {
        DatDatabase Db { get; }

        /// <summary>Retrieves the current iteration of the database.</summary>
        int Iteration { get; }

        /// <summary>Retrieves all file IDs of a specific type.</summary>
        /// <typeparam name="T">The type of database object.</typeparam>
        /// <returns>An enumeration of file IDs.</returns>
        public IEnumerable<uint> GetAllIdsOfType<T>() where T : IDBObj;

        /// <summary>Attempts to retrieve a database object by its file ID.</summary>
        /// <typeparam name="T">The type of database object.</typeparam>
        /// <param name="fileId">The file ID.</param>
        /// <param name="value">The retrieved object, or null if not found.</param>
        /// <returns>True if the object was found; otherwise, false.</returns>
        public bool TryGet<T>(uint fileId, [MaybeNullWhen(false)] out T value) where T : IDBObj;

        /// <summary>Attempts to retrieve the raw bytes of a file by its ID.</summary>
        /// <param name="fileId">The file ID.</param>
        /// <param name="value">The retrieved byte array, or null if not found.</param>
        /// <returns>True if the file was found; otherwise, false.</returns>
        bool TryGetFileBytes(uint fileId, [MaybeNullWhen(false)] out byte[] value);

        /// <summary>Attempts to retrieve the raw bytes of a file by its ID into a provided buffer.</summary>
        /// <param name="fileId">The file ID.</param>
        /// <param name="bytes">The buffer to read into.</param>
        /// <param name="bytesRead">The number of bytes read.</param>
        /// <returns>True if the file was found; otherwise, false.</returns>
        bool TryGetFileBytes(uint fileId, ref byte[] bytes, out int bytesRead);

        /// <summary>Attempts to save a database object.</summary>
        /// <typeparam name="T">The type of database object.</typeparam>
        /// <param name="obj">The object to save.</param>
        /// <param name="iteration">The iteration to save as.</param>
        /// <returns>True if the object was saved; otherwise, false.</returns>
        bool TrySave<T>(T obj, int iteration = 0) where T : IDBObj;
    }
}