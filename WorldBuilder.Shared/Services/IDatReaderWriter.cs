using DatReaderWriter;
using DatReaderWriter.Lib.IO;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace WorldBuilder.Shared.Services {
    /// <summary>
    /// Interface for the dat reader/writer
    /// </summary>
    public interface IDatReaderWriter : IDisposable {
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
    }

    public interface IDatDatabase : IDisposable {
        public IEnumerable<uint> GetAllIdsOfType<T>() where T : IDBObj;
        public bool TryGet<T>(uint fileId, [MaybeNullWhen(false)] out T value) where T : IDBObj;
        bool TryGetFileBytes(uint fileId, [MaybeNullWhen(false)] out byte[] value);
        bool TryGetFileBytes(uint fileId, ref byte[] bytes, out int bytesRead);
    }
}