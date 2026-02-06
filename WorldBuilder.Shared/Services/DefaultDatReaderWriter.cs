using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Lib.IO;
using DatReaderWriter.Options;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;


namespace WorldBuilder.Shared.Services {
    public class DefaultDatReaderWriter : IDatReaderWriter {
        private readonly Dictionary<uint, IDatDatabase> _cellRegions = [];
        private readonly Dictionary<uint, uint> _regionFileMap = [];

        public IDatDatabase Portal { get; }
        public IDatDatabase Language { get; }
        public IDatDatabase HighRes { get; }
        public ReadOnlyDictionary<uint, IDatDatabase> CellRegions => _cellRegions.AsReadOnly();
        public ReadOnlyDictionary<uint, uint> RegionFileMap => _regionFileMap.AsReadOnly();

        public DefaultDatReaderWriter(string datDirectory, DatAccessType accessType = DatAccessType.Read) {
            Portal = new DefaultDatDatabase(new PortalDatabase((options) => {
                options.AccessType = accessType;
                options.FilePath = Path.Combine(datDirectory, "client_portal.dat");
                options.FileCachingStrategy = FileCachingStrategy.Never;
                options.IndexCachingStrategy = IndexCachingStrategy.Never;
            }));

            Language = new DefaultDatDatabase(new LocalDatabase((options) => {
                options.AccessType = accessType;
                options.FilePath = Path.Combine(datDirectory, "client_local_English.dat");
                options.FileCachingStrategy = FileCachingStrategy.Never;
                options.IndexCachingStrategy = IndexCachingStrategy.Never;
            }));

            HighRes = new DefaultDatDatabase(new PortalDatabase((options) => {
                options.AccessType = accessType;
                options.FilePath = Path.Combine(datDirectory, "client_highres.dat");
                options.FileCachingStrategy = FileCachingStrategy.Never;
                options.IndexCachingStrategy = IndexCachingStrategy.Never;
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
                    options.FileCachingStrategy = FileCachingStrategy.Never;
                    options.IndexCachingStrategy = IndexCachingStrategy.Never;
                }));
                _cellRegions.Add(regionId, cell);
                _regionFileMap.Add(regionId, regionFileId);
            }
        }

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

    public class DefaultDatDatabase : IDatDatabase {
        private DatDatabase _db;

        public DefaultDatDatabase(DatDatabase db) {
            _db = db;
        }

        public IEnumerable<uint> GetAllIdsOfType<T>() where T : IDBObj {
            return _db.GetAllIdsOfType<T>();
        }

        public bool TryGet<T>(uint fileId, [MaybeNullWhen(false)] out T value) where T : IDBObj {
            return _db.TryGet(fileId, out value);
        }

        public bool TryGetFileBytes(uint fileId, [MaybeNullWhen(false)] out byte[] value) {
            return _db.TryGetFileBytes(fileId, out value);
        }

        public bool TryGetFileBytes(uint fileId, ref byte[] bytes, out int bytesRead) {
            return _db.TryGetFileBytes(fileId, ref bytes, out bytesRead);
        }

        public void Dispose() {
        }
    }
}