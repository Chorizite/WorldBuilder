using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Lib.IO;
using DatReaderWriter.Types;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Tests.Mocks {
    internal class MockDatReaderWriter : IDatReaderWriter {
        public IDatDatabase Portal { get; } = new MockDatDatabase([
            typeof(Region)
        ]);

        public ReadOnlyDictionary<uint, IDatDatabase> CellRegions { get; } = new(new Dictionary<uint, IDatDatabase>() {
            { 1, new MockDatDatabase([typeof(LandBlock)]) },
            { 65537, new MockDatDatabase([typeof(LandBlock)]) }
        });

        public IDatDatabase HighRes { get; } = new MockDatDatabase([]);

        public IDatDatabase Language { get; } = new MockDatDatabase([]);

        public ReadOnlyDictionary<uint, uint> RegionFileMap { get; } = new(new Dictionary<uint, uint>() {
            { 1, 0x13000001 },
            { 65537, 0x13000001 }
        });

        public string SourceDirectory => "";
        public int PortalIteration => 0;

        public bool TryGetFileBytes(uint regionId, uint fileId, ref byte[] bytes, out int bytesRead) {
            bytesRead = 0;
            return true;
        }

        public bool TrySave<T>(T obj, int iteration = 0) where T : IDBObj {
            return true;
        }

        public bool TrySave<T>(uint regionId, T obj, int iteration = 0) where T : IDBObj {
            return true;
        }

        public IEnumerable<IDatReaderWriter.IdResolution> ResolveId(uint id) {
            return [];
        }

        public void Dispose() {

        }
    }

    internal class MockDatDatabase : IDatDatabase {
        private readonly IEnumerable<Type> _validObjTypes;
        private readonly ConcurrentDictionary<uint, IDBObj?> _cache = new();

        public DatDatabase Db => null!;

        public MockDatDatabase(IEnumerable<Type> validTypes) {
            _validObjTypes = validTypes;
        }

        public int Iteration => 0;

        public bool TrySave<T>(T obj, int iteration = 0) where T : IDBObj {
            return true;
        }

        public IEnumerable<uint> GetAllIdsOfType<T>() where T : IDBObj {
            if (!_validObjTypes.Contains(typeof(T))) throw new Exception($"Invalid type: {typeof(T)}");

            switch (typeof(T)) {
                case Type _ when typeof(T) == typeof(Region): return [0x13000001];
                case Type _ when typeof(T) == typeof(LandBlock):
                    var ids = new List<uint>(254 * 254);
                    for (var x = 0; x < 254; x++) {
                        for (var y = 0; y < 254; y++) {
                            ids.Add((uint)(((x << 8) + y) << 16) + 0xFFFF);
                        }
                    }
                    return ids;
            }

            return [];
        }

        public bool TryGet<T>(uint fileId, [MaybeNullWhen(false)] out T value) where T : IDBObj {
            if (!_validObjTypes.Contains(typeof(T))) throw new Exception($"Invalid type: {typeof(T)}");

            if (_cache.TryGetValue(fileId, out var obj)) {
                if (obj is null) {
                    value = default;
                    return false;
                }

                value = (T)obj;
                return true;
            }

            if (!GetAllIdsOfType<T>().Contains(fileId)) {
                value = default;
                _cache[fileId] = null;
                return false;
            }

            switch (typeof(T)) {
                case Type _ when typeof(T) == typeof(Region): value = (T)(IDBObj)MakeRegion(fileId); return true;
                case Type _ when typeof(T) == typeof(LandBlock): value = (T)(IDBObj)MakeLandBlock(fileId); return true;
            }

            throw new Exception($"Failed to get object of type {typeof(T)} with id 0x{fileId:X8}");
        }

        private static LandBlock MakeLandBlock(uint id) {
            var rand = new Random((int)id);

            return new LandBlock() {
                Id = id,
                Height = [.. Enumerable.Range(0, 81).Select(x => (byte)rand.Next(0, 255))],
                HasObjects = false,
                Terrain = [..Enumerable.Range(0, 81).Select(x => new TerrainInfo() {
                Type = (TerrainTextureType)rand.Next(0, 31),
                Road = (byte)(rand.Next(0, 1000) < 2 ? 1 : 0),
                Scenery = (byte)rand.Next(0, 31),
            })]
            };
        }

        private static Region MakeRegion(uint id) {
            return new Region() {
                Id = id,
                Version = 1,
                LandDefs = new LandDefs() {
                    NumBlockLength = 255,
                    NumBlockWidth = 255,
                    SquareLength = 24,
                    LBlockLength = 8,
                    VertexPerCell = 1,
                    MaxObjHeight = 200,
                    SkyHeight = 1000,
                    RoadWidth = 5,
                    LandHeightTable = [.. Enumerable.Range(0, 255).Select(x => x * 2f)]
                }
            };
        }

        public void Dispose() {

        }

        public bool TryGetFileBytes(uint fileId, [MaybeNullWhen(false)] out byte[] value) {
            value = [];
            return true;
        }

        public bool TryGetFileBytes(uint fileId, ref byte[] bytes, out int bytesRead) {
            bytesRead = 0;
            return true;
        }
    }
}