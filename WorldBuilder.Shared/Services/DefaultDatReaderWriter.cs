using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Lib.IO;
using DatReaderWriter.Options;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;


namespace WorldBuilder.Shared.Services {
    /// <summary>
    /// Default implementation of <see cref="IDatReaderWriter"/>, managing access to multiple dat files.
    /// </summary>
    public class DefaultDatReaderWriter : IDatReaderWriter {
        private readonly Dictionary<uint, IDatDatabase> _cellRegions = [];
        private readonly Dictionary<uint, uint> _regionFileMap = [];

        /// <inheritdoc/>
        public IDatDatabase Portal { get; }
        /// <inheritdoc/>
        public IDatDatabase Language { get; }
        /// <inheritdoc/>
        public IDatDatabase HighRes { get; }
        /// <inheritdoc/>
        public ReadOnlyDictionary<uint, IDatDatabase> CellRegions => _cellRegions.AsReadOnly();
        /// <inheritdoc/>
        public ReadOnlyDictionary<uint, uint> RegionFileMap => _regionFileMap.AsReadOnly();
        /// <inheritdoc/>
        public int PortalIteration => Portal.Iteration;

        private readonly string _datDirectory;

        /// <inheritdoc/>
        public string SourceDirectory => _datDirectory;

        public DefaultDatReaderWriter(string datDirectory, DatAccessType accessType = DatAccessType.Read) {
            _datDirectory = datDirectory;
            Portal = new DefaultDatDatabase(new PortalDatabase((options) => {
                options.AccessType = accessType;
                options.FilePath = Path.Combine(datDirectory, "client_portal.dat");
                options.FileCachingStrategy = FileCachingStrategy.OnDemand;
                options.IndexCachingStrategy = IndexCachingStrategy.OnDemand;
            }));

            Language = new DefaultDatDatabase(new LocalDatabase((options) => {
                options.AccessType = accessType;
                options.FilePath = Path.Combine(datDirectory, "client_local_English.dat");
                options.FileCachingStrategy = FileCachingStrategy.OnDemand;
                options.IndexCachingStrategy = IndexCachingStrategy.OnDemand;
            }));

            HighRes = new DefaultDatDatabase(new PortalDatabase((options) => {
                options.AccessType = accessType;
                options.FilePath = Path.Combine(datDirectory, "client_highres.dat");
                options.FileCachingStrategy = FileCachingStrategy.OnDemand;
                options.IndexCachingStrategy = IndexCachingStrategy.OnDemand;
            }));

            // Load all region cells
            var regions = Portal.GetAllIdsOfType<Region>().ToList();

            foreach (var regionFileId in regions) {
                if (!Portal.TryGet<Region>(regionFileId, out var region)) {
                    throw new Exception($"Failed to load region 0x{regionFileId:X8}");
                }

                var regionId = region.RegionNumber;

                var cellFilePath = Path.Combine(datDirectory, $"client_cell_{regionId}.dat");
                if (!File.Exists(cellFilePath)) {
                    continue;
                }

                var cell = new DefaultDatDatabase(new CellDatabase((options) => {
                    options.AccessType = accessType;
                    options.FilePath = cellFilePath;
                    options.FileCachingStrategy = FileCachingStrategy.OnDemand;
                    options.IndexCachingStrategy = IndexCachingStrategy.OnDemand;
                }));
                _cellRegions.Add(regionId, cell);
                _regionFileMap.Add(regionId, regionFileId);
            }
        }

        /// <inheritdoc/>
        public bool TrySave<T>(T obj, int iteration = 0) where T : IDBObj {
            if (obj is LandBlock) {
                if (_cellRegions.Count == 1) {
                    return _cellRegions.Values.First().TrySave(obj, iteration);
                }
                throw new InvalidOperationException("Multiple cell regions loaded; use TrySave with explicit region ID for LandBlocks.");
            }
            return Portal.TrySave(obj, iteration);
        }

        /// <inheritdoc/>
        public bool TrySave<T>(uint regionId, T obj, int iteration = 0) where T : IDBObj {
            if (obj is LandBlock) {
                if (_cellRegions.TryGetValue(regionId, out var cellDb)) {
                    return cellDb.TrySave(obj, iteration);
                }
                throw new KeyNotFoundException($"Cell region {regionId} not found.");
            }
            return Portal.TrySave(obj, iteration);
        }

        /// <inheritdoc/>
        public bool TryGetFileBytes(uint regionId, uint fileId, ref byte[] bytes, out int bytesRead) {
            if (_cellRegions.TryGetValue(regionId, out var cellDb)) {
                return cellDb.TryGetFileBytes(fileId, ref bytes, out bytesRead);
            }
            return Portal.TryGetFileBytes(fileId, ref bytes, out bytesRead);
        }

        /// <inheritdoc/>
        public IEnumerable<IDatReaderWriter.IdResolution> ResolveId(uint id) {
            var results = new List<IDatReaderWriter.IdResolution>();

            void CheckDb(IDatDatabase db) {
                if (db.Db.Tree.TryGetFile(id, out _)) {
                    var type = db.Db.TypeFromId(id);
                    if (type != DBObjType.Unknown) {
                        results.Add(new IDatReaderWriter.IdResolution(db, type));
                    }
                }
            }

            CheckDb(HighRes);
            CheckDb(Portal);
            CheckDb(Language);
            foreach (var cell in _cellRegions.Values) {
                CheckDb(cell);
            }

            return results;
        }

        /// <inheritdoc/>
        public void Dispose() {
            Portal.Dispose();
            Language.Dispose();
            HighRes.Dispose();
            foreach (var cell in _cellRegions.Values) {
                cell.Dispose();
            }

            _cellRegions.Clear();
        }
    }

    /// <summary>
    /// Default implementation of <see cref="IDatDatabase"/>, wrapping a <see cref="DatDatabase"/>.
    /// </summary>
    public class DefaultDatDatabase : IDatDatabase {
        public DatDatabase Db { get; private set; }
        private readonly ConcurrentDictionary<(Type, uint), IDBObj> _objCache = new();
        private readonly object _lock = new();

        public DefaultDatDatabase(DatDatabase db) {
            Db = db;
        }

        /// <inheritdoc/>
        public int Iteration => Db.Iteration.CurrentIteration;

        public IEnumerable<uint> GetAllIdsOfType<T>() where T : IDBObj {
            lock (_lock) {
                return Db.GetAllIdsOfType<T>().ToList();
            }
        }

        public bool TryGet<T>(uint fileId, [MaybeNullWhen(false)] out T value) where T : IDBObj {
            if (_objCache.TryGetValue((typeof(T), fileId), out var cached)) {
                value = (T)cached;
                return true;
            }
            lock (_lock) {
                if (Db.TryGet<T>(fileId, out value)) {
                    _objCache.TryAdd((typeof(T), fileId), value);
                    return true;
                }
            }
            return false;
        }

        public bool TryGetFileBytes(uint fileId, [MaybeNullWhen(false)] out byte[] value) {
            lock (_lock) {
                return Db.TryGetFileBytes(fileId, out value);
            }
        }

        public bool TryGetFileBytes(uint fileId, ref byte[] bytes, out int bytesRead) {
            lock (_lock) {
                return Db.TryGetFileBytes(fileId, ref bytes, out bytesRead);
            }
        }

        /// <inheritdoc/>
        public bool TrySave<T>(T obj, int iteration = 0) where T : IDBObj {
            lock (_lock) {
                return Db.TryWriteFile(obj, iteration);
            }
        }

        /// <inheritdoc/>
        public void Dispose() {
        }
    }
}