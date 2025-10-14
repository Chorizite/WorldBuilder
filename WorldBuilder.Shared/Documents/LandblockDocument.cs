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
    public static class SceneryHelpers {
        /// <summary>
        /// Displaces a scenery object into a pseudo-randomized location
        /// </summary>
        public static Vector3 Displace(ObjectDesc obj, uint ix, uint iy, uint iq) {
            float x;
            float y;
            float z = obj.BaseLoc.Origin.Z; // Assuming BaseLoc is a struct with X,Y,Z

            var loc = obj.BaseLoc;

            if (obj.DisplaceX <= 0)
                x = loc.Origin.X;
            else
                x = (float)((1813693831 * iy - (iq + 45773) * (1360117743 * iy * ix + 1888038839) - 1109124029 * ix)
                    * 2.3283064e-10 * obj.DisplaceX + loc.Origin.X);

            if (obj.DisplaceY <= 0)
                y = loc.Origin.Y;
            else
                y = (float)((1813693831 * iy - (iq + 72719) * (1360117743 * iy * ix + 1888038839) - 1109124029 * ix)
                    * 2.3283064e-10 * obj.DisplaceY + loc.Origin.Y);

            var quadrant = (1813693831 * iy - ix * (1870387557 * iy + 1109124029) - 402451965) * 2.3283064e-10f;

            if (quadrant >= 0.75) return new Vector3(y, -x, z);
            if (quadrant >= 0.5) return new Vector3(-x, -y, z);
            if (quadrant >= 0.25) return new Vector3(-y, x, z);

            return new Vector3(x, y, z);
        }

        /// <summary>
        /// Returns the scale for a scenery object
        /// </summary>
        public static float ScaleObj(ObjectDesc obj, uint x, uint y, uint k) {
            var scale = 1.0f;

            var minScale = obj.MinScale;
            var maxScale = obj.MaxScale;

            if (minScale == maxScale)
                scale = maxScale;
            else
                scale = (float)(Math.Pow(maxScale / minScale,
                    (1813693831 * y - (k + 32593) * (1360117743 * y * x + 1888038839) - 1109124029 * x) * 2.3283064e-10) * minScale);

            return scale;
        }

        /// <summary>
        /// Returns the rotation for a scenery object as a Quaternion
        /// </summary>
        public static Quaternion RotateObj(ObjectDesc obj, uint x, uint y, uint k, Vector3 loc) {
            var quat = Quaternion.Identity;
            if (obj.MaxRotation > 0.0f) {
                var degrees = (float)((1813693831 * y - (k + 63127) * (1360117743 * y * x + 1888038839) - 1109124029 * x) * 2.3283064e-10 * obj.MaxRotation);
                var radians = MathF.PI / 180f * degrees;
                quat = Quaternion.CreateFromYawPitchRoll(radians, 0, 0); // Heading around Z
            }
            return quat;
        }

        /// <summary>
        /// Aligns an object to a plane, returning Quaternion
        /// </summary>
        public static Quaternion ObjAlign(ObjectDesc obj, Vector3 normal, float z, Vector3 loc) {
            // Approximate alignment: create quaternion that aligns Z to normal
            var up = Vector3.UnitZ;
            var axis = Vector3.Cross(up, normal);
            if (axis.LengthSquared() < 1e-6f) return Quaternion.Identity;
            axis = Vector3.Normalize(axis);
            var angle = MathF.Acos(Vector3.Dot(up, normal));
            return Quaternion.CreateFromAxisAngle(axis, angle);
        }

        /// <summary>
        /// Returns TRUE if floor slope is within bounds for this object
        /// </summary>
        public static bool CheckSlope(ObjectDesc obj, float zNormal) {
            return zNormal >= obj.MinSlope && zNormal <= obj.MaxSlope;
        }
    }


    [MemoryPack.MemoryPackable]
    public partial struct StaticObject {
        public uint Id; // GfxObj or Setup ID
        public bool IsSetup; // True for Setup, false for GfxObj
        public Vector3 Origin; // World-space position
        public Quaternion Orientation; // World-space rotation

        public Vector3 Scale { get; internal set; }
    }

    [MemoryPack.MemoryPackable]
    public partial class LandblockData {
        public List<StaticObject> StaticObjects = new();
    }

    public partial class LandblockDocument : BaseDocument {
        public override string Type => nameof(LandblockDocument);

        [MemoryPackInclude]
        private LandblockData _data = new();

        private TerrainDocument _terrainDoc;

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
                        Orientation = obj.Frame.Orientation
                    });
                }

                foreach (var building in lbi.Buildings) {
                    _data.StaticObjects.Add(new StaticObject {
                        Id = building.ModelId,
                        IsSetup = (building.ModelId & 0x02000000) != 0,
                        Origin = Offset(building.Frame.Origin, lbId),
                        Orientation = building.Frame.Orientation
                    });
                }
            }
            
            _terrainDoc = await documentManager.GetOrCreateDocumentAsync<TerrainDocument>("terrain");
            if (_terrainDoc == null) {
                _logger.LogError("Failed to load TerrainDocument");
                return false;
            }
            var region = datreader.Dats.Portal.Region;
            GenerateSceneryAsync(region, datreader, lbId);
            ClearDirty();
            return true;
        }

        private void GenerateSceneryAsync(Region region, IDatReaderWriter datreader, uint lbId) {
            var lbKey = (ushort)lbId;
            var lbTerrainEntries = _terrainDoc.GetLandblock(lbKey);
            if (lbTerrainEntries == null) {
                Console.WriteLine("Failed to load landblock {LandblockId}", lbKey);
                return;
            }

            var regionDesc = datreader.Dats.Portal.Region;
            if (regionDesc == null) {
                Console.WriteLine("Failed to load region");
                return;
            }

            var buildings = new HashSet<int>();
            var lbGlobalX = (lbId >> 8) & 0xFF;
            var lbGlobalY = lbId & 0xFF;
            foreach (var b in _data.StaticObjects) {
                var localX = b.Origin.X - lbGlobalX * 192f;
                var localY = b.Origin.Y - lbGlobalY * 192f;
                var cellX = (int)MathF.Floor(localX / 24f);
                var cellY = (int)MathF.Floor(localY / 24f);
                if (cellX >= 0 && cellX < 8 && cellY >= 0 && cellY < 8) {
                    buildings.Add(cellX * 9 + cellY);
                }
            }

            var blockCellX = (int)lbGlobalX * 8;
            var blockCellY = (int)lbGlobalY * 8;

            for (int i = 0; i < lbTerrainEntries.Length; i++) {
                var entry = lbTerrainEntries[i];

                var terrainType = entry.Type;
                var sceneType = entry.Scenery;

                if (terrainType >= regionDesc.TerrainInfo.TerrainTypes.Count) continue;
                var terrainInfo = regionDesc.TerrainInfo.TerrainTypes[(int)terrainType];
                if (sceneType >= terrainInfo.SceneTypes.Count) continue;
                var sceneInfoIdx = terrainInfo.SceneTypes[(int)sceneType];
                var sceneInfo = regionDesc.SceneInfo.SceneTypes[(int)sceneInfoIdx];
                if (sceneInfo.Scenes.Count == 0) {
                    continue;
                }

                var cellX = i / 9;
                var cellY = i % 9;
                var globalCellX = (uint)(blockCellX + cellX);
                var globalCellY = (uint)(blockCellY + cellY);

                var cellMat = globalCellY * (712977289u * globalCellX + 1813693831u) - 1109124029u * globalCellX + 2139937281u;
                var offset = cellMat * 2.3283064e-10f;
                var sceneIdx = (int)(sceneInfo.Scenes.Count * offset);
                sceneIdx = Math.Clamp(sceneIdx, 0, sceneInfo.Scenes.Count - 1);
                var sceneId = sceneInfo.Scenes[sceneIdx];

                if (!datreader.TryGet<Scene>(sceneId, out var scene) || scene.Objects.Count == 0) {
                    continue;
                }

                if (entry.Road != 0) {
                    continue;
                }
                if (buildings.Contains(i)) {
                    continue;
                }

                var cellXMat = -1109124029 * (int)globalCellX;
                var cellYMat = 1813693831 * (int)globalCellY;
                var cellMat2 = 1360117743 * globalCellX * globalCellY + 1888038839;

                for (uint j = 0; j < scene.Objects.Count; j++) {
                    var obj = scene.Objects[(int)j];
                    if (obj.ObjectId == 0) {
                        continue;
                    }

                    var noise = (uint)(cellXMat + cellYMat - cellMat2 * (23399 + j)) * 2.3283064e-10f;
                    if (noise >= obj.Frequency) continue;

                    var localPos = SceneryHelpers.Displace(obj, globalCellX, globalCellY, j);
                    var lx = cellX * 24f + localPos.X;
                    var ly = cellY * 24f + localPos.Y;
                    if (lx < 0 || ly < 0 || lx >= 192f || ly >= 192f) {
                        continue;
                    }

                    if (OnRoad(new Vector3(lx, ly, 0), lbTerrainEntries)) {
                        continue;
                    }

                    var fracX = (localPos.X % 24f) / 24f;
                    var fracY = (localPos.Y % 24f) / 24f;
                    var z = InterpolateHeight(region, lbTerrainEntries, cellX, cellY, fracX, fracY) + localPos.Z;
                    localPos.Z = z;

                    var normal = GetCellNormal(region, lbTerrainEntries, cellX, cellY);
                    if (!SceneryHelpers.CheckSlope(obj, normal.Z)) {
                        continue;
                    }

                    Quaternion quat;
                    if (obj.Align != 0) {
                        quat = SceneryHelpers.ObjAlign(obj, normal, z, localPos);
                    }
                    else {
                        quat = SceneryHelpers.RotateObj(obj, globalCellX, globalCellY, j, localPos);
                    }

                    var scaleVal = SceneryHelpers.ScaleObj(obj, globalCellX, globalCellY, j);
                    var scale = new Vector3(scaleVal);

                    var worldOrigin = Offset(localPos, lbId) + new Vector3(cellX * 24f, cellY * 24f, 0);

                    _data.StaticObjects.Add(new StaticObject {
                        Id = obj.ObjectId,
                        Origin = worldOrigin,
                        Orientation = quat,
                        IsSetup = (obj.ObjectId & 0x02000000) != 0,
                        Scale = scale
                    });
                }
            }
        }

        private const float TileLength = 24f;
        private const float RoadWidth = 5f;

        private bool OnRoad(Vector3 obj, TerrainEntry[] entries) {
            int x = (int)(obj.X / TileLength);
            int y = (int)(obj.Y / TileLength);

            float rMin = RoadWidth;
            float rMax = TileLength - RoadWidth;

            int x0 = x;
            int x1 = x0 + 1;
            int y0 = y;
            int y1 = y0 + 1;

            uint r0 = GetRoad(entries, x0, y0);
            uint r1 = GetRoad(entries, x0, y1);
            uint r2 = GetRoad(entries, x1, y0);
            uint r3 = GetRoad(entries, x1, y1);

            if (r0 == 0 && r1 == 0 && r2 == 0 && r3 == 0)
                return false;

            float dx = obj.X - x * TileLength;
            float dy = obj.Y - y * TileLength;

            if (r0 > 0) {
                if (r1 > 0) {
                    if (r2 > 0) {
                        if (r3 > 0)
                            return true;
                        else
                            return (dx < rMin || dy < rMin);
                    }
                    else {
                        if (r3 > 0)
                            return (dx < rMin || dy > rMax);
                        else
                            return (dx < rMin);
                    }
                }
                else {
                    if (r2 > 0) {
                        if (r3 > 0)
                            return (dx > rMax || dy < rMin);
                        else
                            return (dy < rMin);
                    }
                    else {
                        if (r3 > 0)
                            return (Math.Abs(dx - dy) < rMin);
                        else
                            return (dx + dy < rMin);
                    }
                }
            }
            else {
                if (r1 > 0) {
                    if (r2 > 0) {
                        if (r3 > 0)
                            return (dx > rMax || dy > rMax);
                        else
                            return (Math.Abs(dx + dy - TileLength) < rMin);
                    }
                    else {
                        if (r3 > 0)
                            return (dy > rMax);
                        else
                            return (TileLength + dx - dy < rMin);
                    }
                }
                else {
                    if (r2 > 0) {
                        if (r3 > 0)
                            return (dx > rMax);
                        else
                            return (TileLength - dx + dy < rMin);
                    }
                    else {
                        if (r3 > 0)
                            return (TileLength * 2f - dx - dy < rMin);
                        else
                            return false;
                    }
                }
            }
        }

        private uint GetRoad(TerrainEntry[] entries, int x, int y) {
            if (x < 0 || y < 0 || x >= 9 || y >= 9) return 0;
            var idx = x * 9 + y;
            if (idx >= entries.Length) return 0;
            var road = entries[idx].Road;
            return (uint)(road & 0x3); // Lower 2 bits
        }

        private float InterpolateHeight(Region region, TerrainEntry[] entries, int cellX, int cellY, float fracX, float fracY) {
            var h00 = GetHeight(region, entries, cellX, cellY);
            var h10 = GetHeight(region, entries, cellX + 1, cellY);
            var h01 = GetHeight(region, entries, cellX, cellY + 1);
            var h11 = GetHeight(region, entries, cellX + 1, cellY + 1);

            var h0 = h00 + fracX * (h10 - h00);
            var h1 = h01 + fracX * (h11 - h01);
            return h0 + fracY * (h1 - h0);
        }

        private float GetHeight(Region region, TerrainEntry[] entries, int x, int y) {
            if (x < 0 || y < 0 || x >= 9 || y >= 9) return 0;
            return region.LandDefs.LandHeightTable[entries[x * 9 + y].Height];
        }

        private Vector3 GetCellNormal(Region region, TerrainEntry[] entries, int cellX, int cellY) {
            var hLeft = GetHeight(region, entries, cellX - 1, cellY);
            var hRight = GetHeight(region, entries, cellX + 1, cellY);
            var hUp = GetHeight(region, entries, cellX, cellY - 1);
            var hDown = GetHeight(region, entries, cellX, cellY + 1);

            const float HeightScale = 0.5f;
            const float InvCell = 1f / (2f * 24f);

            var dzdx = (hRight - hLeft) * HeightScale * InvCell;
            var dzdy = (hDown - hUp) * HeightScale * InvCell;
            var normal = new Vector3(-dzdx, -dzdy, 1f);
            return Vector3.Normalize(normal);
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
            /*
            if (evt is TerrainUpdateEvent terrainUpdate && terrainUpdate.Changes.ContainsKey((ushort)uint.Parse(Id.Replace("landblock_", ""), System.Globalization.NumberStyles.HexNumber))) {
                Task.Run(async () => {
                    await GenerateSceneryAsync(Dats, uint.Parse(Id.Replace("landblock_", ""), System.Globalization.NumberStyles.HexNumber));
                    MarkDirty();
                    OnUpdate(this);
                });
            }
            */
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