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
    [MemoryPack.MemoryPackable]
    public partial struct StaticObject {
        public uint Id; // GfxObj or Setup ID
        public bool IsSetup; // True for Setup, false for GfxObj
        public Vector3 Origin; // World-space position
        public Quaternion Orientation; // World-space rotation
    }

    // Updated LandblockDocument to store StaticObject list
    [MemoryPack.MemoryPackable]
    public partial class LandblockData {
        public List<StaticObject> StaticObjects = new();
    }

    public partial class LandblockDocument : BaseDocument {
        public override string Type => nameof(LandblockDocument);

        [MemoryPackInclude]
        private LandblockData _data = new();

        public LandblockDocument(ILogger logger) : base(logger) { }

        protected override async Task<bool> InitInternal(IDatReaderWriter datreader) {
            var lbId = uint.Parse(Id.Replace("landblock_", ""), System.Globalization.NumberStyles.HexNumber);
            lbId = lbId << 16 | 0xFFFE;
            if (!datreader.TryGet<LandBlockInfo>(lbId, out var lbi)) {
                return true;
            }
            _data.StaticObjects.Clear();
            foreach (var obj in lbi.Objects) {
                _data.StaticObjects.Add(new StaticObject {
                    Id = obj.Id,
                    IsSetup = (obj.Id & 0x02000000) != 0,
                    Origin = Offset(obj.Frame.Origin),
                    Orientation = obj.Frame.Orientation
                });
            }
            foreach (var building in lbi.Buildings) {
                _data.StaticObjects.Add(new StaticObject {
                    Id = building.ModelId,
                    IsSetup = (building.ModelId & 0x02000000) != 0,
                    Origin = Offset(building.Frame.Origin),
                    Orientation = building.Frame.Orientation
                });
            }
            ClearDirty();
            return true;
        }

        private Vector3 Offset(Vector3 origin) {
            var lb = uint.Parse(Id.Replace("landblock_", ""), System.Globalization.NumberStyles.HexNumber);
            return new Vector3(((lb >> 8) & 0xFF) * 192f + origin.X, (lb & 0xFF) * 192f + origin.Y, origin.Z);
        }

        protected override byte[] SaveToProjectionInternal() {
            var projection = MemoryPackSerializer.Serialize(_data);
            ClearDirty();
            return projection;
        }

        protected override bool LoadFromProjectionInternal(byte[] projection) {
            _data = MemoryPackSerializer.Deserialize<LandblockData>(projection) ?? new();
            ClearDirty();
            return true;
        }

        protected override Task<bool> SaveToDatsInternal(IDatReaderWriter datwriter, int iteration = 0) {
            throw new NotImplementedException(); // Implement if needed
        }

        public void Apply(TerrainUpdateEvent update) {
            // Example: Handle static object add/remove via update
            if (update is StaticObjectUpdateEvent staticUpdate) {
                if (staticUpdate.IsAdd) {
                    _data.StaticObjects.Add(staticUpdate.Object);
                }
                else {
                    _data.StaticObjects.RemoveAll(o => o.Id == staticUpdate.Object.Id && o.Origin == staticUpdate.Object.Origin);
                }
                MarkDirty();
            }
        }

        public IEnumerable<(Vector3 Position, Quaternion Rotation)> GetStaticSpawns() {
            foreach (var obj in _data.StaticObjects) {
                yield return (obj.Origin, obj.Orientation);
            }
        }

        public IEnumerable<StaticObject> GetStaticObjects() => _data.StaticObjects;
    }
    public class StaticObjectUpdateEvent : TerrainUpdateEvent {
        public StaticObject Object { get; }
        public bool IsAdd { get; } // True for add, false for remove

        public StaticObjectUpdateEvent(StaticObject obj, bool isAdd) {
            Object = obj;
            IsAdd = isAdd;
        }
    }

}