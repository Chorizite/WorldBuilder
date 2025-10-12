using CommunityToolkit.Mvvm.DependencyInjection;
using DatReaderWriter;
using DatReaderWriter.DBObjs;
using MemoryPack;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Shared.Documents {
    [MemoryPackable]
    public partial class LandblockData {
        public List<Vector3> StaticSpawns = new();
    }

    public partial class LandblockDocument : BaseDocument {
        public override string Type => nameof(LandblockDocument);

        [MemoryPackInclude]
        private LandblockData _data = new();

        public LandblockDocument(ILogger logger) : base(logger) { }

        protected override async Task<bool> InitInternal(IDatReaderWriter datreader) {
            // Load from DATs (similar to TerrainDocument)
            var lbId = uint.Parse(Id.Replace("landblock_", ""), System.Globalization.NumberStyles.HexNumber);
            lbId = lbId << 16 | 0xFFFE;
            if (!datreader.TryGet<LandBlockInfo>(lbId, out var lbi)) {
                return true;
            }
            _data.StaticSpawns.AddRange(lbi.Objects.Select(o => Offset(o.Frame.Origin)).Concat(lbi.Buildings.Select(b => Offset(b.Frame.Origin))));
            return true;
        }

        private Vector3 Offset(Vector3 origin) {
            // Offset by landblock position
            var lb = uint.Parse(Id.Replace("landblock_", ""), System.Globalization.NumberStyles.HexNumber);

            return new Vector3((lb >> 8) * 192f + origin.X, (lb & 0xFF) * 192f + origin.Y, 0f);

        }

        protected override byte[] SaveToProjectionInternal() {
            return MemoryPackSerializer.Serialize(_data);
        }

        protected override bool LoadFromProjectionInternal(byte[] projection) {
            _data = MemoryPackSerializer.Deserialize<LandblockData>(projection) ?? new();
            return true;
        }

        protected override Task<bool> SaveToDatsInternal(IDatReaderWriter datwriter, int iteration = 0) {
           // var lbId = uint.Parse(Id.Replace("landblock_", ""), System.Globalization.NumberStyles.HexNumber);
           // return Task.FromResult(datwriter.TrySave(LandBlockInfo, iteration));
           throw new NotImplementedException();
        }

        // Add methods for rendering/access
        public IEnumerable<(Vector3 Position, Quaternion Rotation)> GetStaticSpawns() {
            foreach (var stab in _data.StaticSpawns) {
                yield return (stab, Quaternion.Identity);
            }
        }
    }
}