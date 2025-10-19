using CommunityToolkit.Mvvm.DependencyInjection;
using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Options;
using DatReaderWriter.Types;
using MemoryPack;
using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace WorldBuilder.Shared.Documents {
    [MemoryPack.MemoryPackable]
    public partial struct StaticObject {
        public uint Id; // GfxObj or Setup ID
        public bool IsSetup; // True for Setup, false for GfxObj
        public Vector3 Origin; // World-space position
        public Quaternion Orientation; // World-space rotation
        public Vector3 Scale;
    }

    [MemoryPack.MemoryPackable]
    public partial class LandblockData {
        public List<StaticObject> StaticObjects = new();
    }

    public partial class LandblockDocument : BaseDocument {
        public override string Type => nameof(LandblockDocument);

        [MemoryPackInclude]
        private LandblockData _data = new();

        public LandblockDocument(ILogger logger) : base(logger) {
        }

        protected override async Task<bool> InitInternal(IDatReaderWriter datreader, DocumentManager documentManager) {
            var lbIdHex = Id.Replace("landblock_", "");
            var lbId = uint.Parse(lbIdHex, System.Globalization.NumberStyles.HexNumber);
            var infoId = lbId << 16 | 0xFFFE;

            if (datreader.TryGet<LandBlockInfo>(infoId, out var lbi)) {
                foreach (var obj in lbi.Objects) {
                    _data.StaticObjects.Add(new StaticObject {
                        Id = obj.Id,
                        IsSetup = (obj.Id & 0x02000000) != 0,
                        Origin = Offset(obj.Frame.Origin, lbId),
                        Orientation = obj.Frame.Orientation,
                        Scale = Vector3.One
                    });
                }

                foreach (var building in lbi.Buildings) {
                    _data.StaticObjects.Add(new StaticObject {
                        Id = building.ModelId,
                        IsSetup = (building.ModelId & 0x02000000) != 0,
                        Origin = Offset(building.Frame.Origin, lbId),
                        Orientation = building.Frame.Orientation,
                        Scale = Vector3.One
                    });
                }
            }

            ClearDirty();
            return true;
        }

        private Vector3 Offset(Vector3 origin, uint lbId) {
            var blockX = (lbId >> 8) & 0xFF;
            var blockY = lbId & 0xFF;
            return new Vector3(blockX * 192f + origin.X, blockY * 192f + origin.Y, origin.Z);
        }

        protected override byte[] SaveToProjectionInternal() {
            return MemoryPackSerializer.Serialize(_data);
        }

        protected override bool LoadFromProjectionInternal(byte[] projection) {
            _data = MemoryPackSerializer.Deserialize<LandblockData>(projection) ?? new();
            return true;
        }

        protected override Task<bool> SaveToDatsInternal(IDatReaderWriter datwriter, int iteration = 0) {
            throw new NotImplementedException();
        }

        public bool Apply(BaseDocumentEvent evt) {
            return true;
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