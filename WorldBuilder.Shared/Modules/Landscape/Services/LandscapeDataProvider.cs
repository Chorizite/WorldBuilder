using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;
using MemoryPack;
using Microsoft.Extensions.Logging;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Repositories;
using WorldBuilder.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace WorldBuilder.Shared.Modules.Landscape.Services {
    /// <summary>
    /// Implements the unified landscape data provider.
    /// </summary>
    public class LandscapeDataProvider : ILandscapeDataProvider {
        private readonly IProjectRepository _repo;
        private readonly ILogger? _log;

        public LandscapeDataProvider(IProjectRepository repo, ILoggerFactory? loggerFactory = null) {
            _repo = repo;
            _log = loggerFactory?.CreateLogger<LandscapeDataProvider>();
        }

        /// <inheritdoc/>
        public async Task<MergedLandblock> GetMergedLandblockAsync(ushort landblockId, IDatDatabase? cellDatabase, IDatDatabase? portalDatabase, IEnumerable<string> visibleLayerIds, string? baseLayerId, CancellationToken ct) {
            var merged = new MergedLandblock();
            var visibleLayers = new HashSet<string>(visibleLayerIds);
            var effectiveBaseLayerId = baseLayerId ?? string.Empty;

            // 1. Parse base from DAT
            var lbFileId = ((uint)landblockId << 16) | 0xFFFE;

            if (cellDatabase != null) {
                if (cellDatabase.TryGet<LandBlockInfo>(lbFileId, out var lbi) && lbi != null) {
                    for (uint i = 0; i < lbi.NumCells; i++) {
                        merged.EnvCellIds.Add(((uint)landblockId << 16) | (0x0100 + i));
                    }

                    for (int i = 0; i < (lbi.Objects?.Count ?? 0); i++) {
                        var stab = lbi.Objects![i];
                        if (stab == null) continue;

                        ulong instanceId = InstanceIdConstants.EncodeStaticObject(lbFileId, (ushort)i);
                        merged.StaticObjects[instanceId] = new StaticObject {
                            SetupId = stab.Id,
                            Position = stab.Frame?.Origin ?? Vector3.Zero,
                            Rotation = stab.Frame?.Orientation ?? Quaternion.Identity,
                            InstanceId = instanceId,
                            LayerId = effectiveBaseLayerId
                        };
                    }

                    for (int i = 0; i < (lbi.Buildings?.Count ?? 0); i++) {
                        var bldg = lbi.Buildings![i];
                        if (bldg == null) continue;

                        ulong instanceId = InstanceIdConstants.EncodeBuilding(lbFileId, (ushort)i);
                        merged.Buildings[instanceId] = new BuildingObject {
                            ModelId = bldg.ModelId,
                            Position = bldg.Frame?.Origin ?? Vector3.Zero,
                            Rotation = bldg.Frame?.Orientation ?? Quaternion.Identity,
                            InstanceId = instanceId,
                            LayerId = effectiveBaseLayerId
                        };
                    }
                }
            }

            // 2. Apply active layers (overrides from repository)
            var repoObjects = await _repo.GetStaticObjectsAsync(landblockId, null, null, ct);
            if (repoObjects != null) {
                foreach (var obj in repoObjects) {
                    if (!visibleLayers.Contains(obj.LayerId)) continue;
                    if (obj.CellId.HasValue) continue;

                    if (obj.IsDeleted) {
                        merged.StaticObjects.Remove(obj.InstanceId);
                    } else {
                        merged.StaticObjects[obj.InstanceId] = obj;
                    }
                }
            }

            var repoBuildings = await _repo.GetBuildingsAsync(landblockId, null, ct);
            if (repoBuildings != null) {
                foreach (var bldg in repoBuildings) {
                    if (!visibleLayers.Contains(bldg.LayerId)) continue;
                    if (bldg.IsDeleted) {
                        merged.Buildings.Remove(bldg.InstanceId);
                    } else {
                        merged.Buildings[bldg.InstanceId] = bldg;
                    }
                }
            }

            var repoCells = await _repo.GetEnvCellIdsForLandblocksAsync(new[] { landblockId }, null, ct);
            if (repoCells != null && repoCells.TryGetValue(landblockId, out var cells)) {
                foreach (var cellId in cells) {
                    if (!merged.EnvCellIds.Contains(cellId)) {
                        merged.EnvCellIds.Add(cellId);
                    }
                }
            }

            return merged;
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyDictionary<ushort, MergedLandblock>> GetMergedLandblocksAsync(IEnumerable<ushort> landblockIds, IDatDatabase? cellDatabase, IDatDatabase? portalDatabase, IEnumerable<string> visibleLayerIds, string? baseLayerId, CancellationToken ct) {
            var results = new Dictionary<ushort, MergedLandblock>();
            var ids = landblockIds.ToList();
            if (ids.Count == 0) return results;

            var visibleLayers = new HashSet<string>(visibleLayerIds);
            var effectiveBaseLayerId = baseLayerId ?? string.Empty;

            // 1. Parse base from DAT for all landblocks
            foreach (var landblockId in ids) {
                var merged = new MergedLandblock();
                results[landblockId] = merged;

                var lbFileId = ((uint)landblockId << 16) | 0xFFFE;

                if (cellDatabase != null) {
                    if (cellDatabase.TryGet<LandBlockInfo>(lbFileId, out var lbi) && lbi != null) {
                        for (uint i = 0; i < lbi.NumCells; i++) {
                            merged.EnvCellIds.Add(((uint)landblockId << 16) | (0x0100 + i));
                        }

                        for (int i = 0; i < (lbi.Objects?.Count ?? 0); i++) {
                            var stab = lbi.Objects![i];
                            if (stab == null) continue;

                            ulong instanceId = InstanceIdConstants.EncodeStaticObject(lbFileId, (ushort)i);
                            merged.StaticObjects[instanceId] = new StaticObject {
                                SetupId = stab.Id,
                                Position = stab.Frame?.Origin ?? Vector3.Zero,
                                Rotation = stab.Frame?.Orientation ?? Quaternion.Identity,
                                InstanceId = instanceId,
                                LayerId = effectiveBaseLayerId
                            };
                        }

                        for (int i = 0; i < (lbi.Buildings?.Count ?? 0); i++) {
                            var bldg = lbi.Buildings![i];
                            if (bldg == null) continue;

                            ulong instanceId = InstanceIdConstants.EncodeBuilding(lbFileId, (ushort)i);
                            merged.Buildings[instanceId] = new BuildingObject {
                                ModelId = bldg.ModelId,
                                Position = bldg.Frame?.Origin ?? Vector3.Zero,
                                Rotation = bldg.Frame?.Orientation ?? Quaternion.Identity,
                                InstanceId = instanceId,
                                LayerId = effectiveBaseLayerId
                            };
                        }
                    }
                }
            }

            // 2. Apply active layers (overrides from repository) in batch
            var repoObjects = await _repo.GetStaticObjectsForLandblocksAsync(ids, null, ct);
            if (repoObjects != null) {
                foreach (var kvp in repoObjects) {
                    if (results.TryGetValue(kvp.Key, out var merged)) {
                        foreach (var obj in kvp.Value) {
                            if (!visibleLayers.Contains(obj.LayerId)) continue;
                            if (obj.CellId.HasValue) continue;

                            if (obj.IsDeleted) {
                                merged.StaticObjects.Remove(obj.InstanceId);
                            } else {
                                merged.StaticObjects[obj.InstanceId] = obj;
                            }
                        }
                    }
                }
            }

            var repoBuildings = await _repo.GetBuildingsForLandblocksAsync(ids, null, ct);
            if (repoBuildings != null) {
                foreach (var kvp in repoBuildings) {
                    if (results.TryGetValue(kvp.Key, out var merged)) {
                        foreach (var bldg in kvp.Value) {
                            if (!visibleLayers.Contains(bldg.LayerId)) continue;
                            if (bldg.IsDeleted) {
                                merged.Buildings.Remove(bldg.InstanceId);
                            } else {
                                merged.Buildings[bldg.InstanceId] = bldg;
                            }
                        }
                    }
                }
            }

            var repoCells = await _repo.GetEnvCellIdsForLandblocksAsync(ids, null, ct);
            if (repoCells != null) {
                foreach (var kvp in repoCells) {
                    if (results.TryGetValue(kvp.Key, out var merged)) {
                        foreach (var cellId in kvp.Value) {
                            if (!merged.EnvCellIds.Contains(cellId)) {
                                merged.EnvCellIds.Add(cellId);
                            }
                        }
                    }
                }
            }

            return results;
        }

        /// <inheritdoc/>
        public async Task<Cell> GetMergedEnvCellAsync(uint cellId, IDatDatabase? cellDatabase, IDatDatabase? portalDatabase, IEnumerable<string> visibleLayerIds, string? baseLayerId, CancellationToken ct) {
            var properties = new Cell();
            var visibleLayers = new HashSet<string>(visibleLayerIds);
            var effectiveBaseLayerId = baseLayerId ?? string.Empty;

            if (cellDatabase != null && cellDatabase.TryGet<EnvCell>(cellId, out var cell)) {
                var pos = cell.Position.Origin;
                var rot = cell.Position.Orientation;

                properties = new Cell {
                    EnvironmentId = cell.EnvironmentId,
                    Flags = (uint)cell.Flags,
                    CellStructure = cell.CellStructure,
                    Position = pos,
                    Rotation = rot,
                    Surfaces = new List<ushort>(cell.Surfaces),
                    Portals = cell.CellPortals.Select(p => new WbCellPortal(p)).ToList(),
                    LayerId = effectiveBaseLayerId,
                    CellId = cellId
                };

                // Logical bounding box calculation from DAT geometry
                if (portalDatabase != null) {
                    uint envRecordId = 0x0D000000u | cell.EnvironmentId;
                    if (portalDatabase.TryGet<DatReaderWriter.DBObjs.Environment>(envRecordId, out var environment)) {
                        if (environment.Cells.TryGetValue(cell.CellStructure, out var cellStruct)) {
                            Vector3 min = new Vector3(float.MaxValue);
                            Vector3 max = new Vector3(float.MinValue);
                            foreach (var vert in cellStruct.VertexArray.Vertices.Values) {
                                var worldV = Vector3.Transform(vert.Origin, Matrix4x4.CreateFromQuaternion(rot)) + pos;
                                min = Vector3.Min(min, worldV);
                                max = Vector3.Max(max, worldV);
                            }
                            properties.MinBounds = min;
                            properties.MaxBounds = max;
                        }
                    }
                }

                if (cell.StaticObjects != null) {
                    for (int i = 0; i < cell.StaticObjects.Count; i++) {
                        var stab = cell.StaticObjects[i];
                        if (stab == null) continue;

                        ulong instanceId = InstanceIdConstants.EncodeEnvCellStaticObject(cellId, (ushort)i, false);
                        properties.StaticObjects[instanceId] = new StaticObject {
                            SetupId = stab.Id,
                            Position = stab.Frame?.Origin ?? Vector3.Zero,
                            Rotation = stab.Frame?.Orientation ?? Quaternion.Identity,
                            InstanceId = instanceId,
                            LayerId = effectiveBaseLayerId,
                            CellId = cellId
                        };
                    }
                }
            }

            // 2. Apply active layers (overrides from repository)
            var repoResult = await _repo.GetEnvCellAsync(cellId, null, ct);
            if (repoResult.IsSuccess) {
                var repoCell = repoResult.Value;
                // Merge properties
                properties = new Cell {
                    EnvironmentId = repoCell.EnvironmentId,
                    Flags = repoCell.Flags,
                    CellStructure = repoCell.CellStructure,
                    Position = repoCell.Position,
                    Rotation = repoCell.Rotation,
                    Surfaces = repoCell.Surfaces,
                    Portals = repoCell.Portals,
                    LayerId = repoCell.LayerId,
                    StaticObjects = properties.StaticObjects,
                    CellId = repoCell.CellId,
                    MinBounds = repoCell.MinBounds,
                    MaxBounds = repoCell.MaxBounds
                };

                // If the repository didn't have bounds (or they are empty), and we have a portal database,
                // try to calculate them from the environment record.
                if ((properties.MinBounds == Vector3.Zero && properties.MaxBounds == Vector3.Zero) && portalDatabase != null) {
                    uint envRecordId = 0x0D000000u | properties.EnvironmentId;
                    if (portalDatabase.TryGet<DatReaderWriter.DBObjs.Environment>(envRecordId, out var environment)) {
                        if (environment.Cells.TryGetValue(properties.CellStructure, out var cellStruct)) {
                            Vector3 min = new Vector3(float.MaxValue);
                            Vector3 max = new Vector3(float.MinValue);
                            foreach (var vert in cellStruct.VertexArray.Vertices.Values) {
                                var worldV = Vector3.Transform(vert.Origin, Matrix4x4.CreateFromQuaternion(properties.Rotation)) + properties.Position;
                                min = Vector3.Min(min, worldV);
                                max = Vector3.Max(max, worldV);
                            }
                            properties.MinBounds = min;
                            properties.MaxBounds = max;
                        }
                    }
                }
            }

            // Sync objects from relational table
            var repoObjects = await _repo.GetStaticObjectsAsync(null, cellId, null, ct);
            foreach (var obj in repoObjects) {
                if (!visibleLayers.Contains(obj.LayerId)) continue;
                if (obj.IsDeleted) {
                    properties.StaticObjects.Remove(obj.InstanceId);
                } else {
                    properties.StaticObjects[obj.InstanceId] = obj;
                }
            }

            return properties;
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyDictionary<uint, TerrainEntry>> GetMergedTerrainAsync(uint regionId, IEnumerable<string> visibleLayerIds, ITerrainInfo regionInfo, CancellationToken ct) {
            var mergedChanges = new Dictionary<uint, TerrainEntry>();
            var visibleLayers = new HashSet<string>(visibleLayerIds);

            // Fetch all terrain patches for the region
            var patches = await _repo.GetTerrainPatchesAsync(regionId, null, ct);
            if (patches == null || patches.Count == 0) {
                return mergedChanges;
            }

            int mapWidth = regionInfo.MapWidthInVertices;
            int vertexStride = regionInfo.LandblockVerticeLength;
            int strideMinusOne = vertexStride - 1;

            foreach (var patch in patches) {
                if (patch.Data == null || patch.Data.Length == 0) continue;

                try {
                    var doc = BaseDocument.Deserialize<TerrainPatchDocument>(patch.Data);
                    if (doc == null || doc.LayerEdits == null) continue;

                    var parts = patch.Id.Split('_');
                    if (parts.Length < 4) continue;
                    if (!uint.TryParse(parts[2], out var chunkX) || !uint.TryParse(parts[3], out var chunkY)) continue;

                    int chunkBaseVx = (int)(chunkX * LandscapeChunk.LandblocksPerChunk * strideMinusOne);
                    int chunkBaseVy = (int)(chunkY * LandscapeChunk.LandblocksPerChunk * strideMinusOne);

                    foreach (var layerId in visibleLayers) {
                        if (doc.LayerEdits.TryGetValue(layerId, out var layerVertices) && layerVertices != null) {
                            for (int i = 0; i < layerVertices.Length; i++) {
                                var entry = layerVertices[i];
                                if (entry.Flags == TerrainEntryFlags.None) continue;

                                int localY = i / LandscapeChunk.ChunkVertexStride;
                                int localX = i % LandscapeChunk.ChunkVertexStride;

                                uint globalVertexIndex = (uint)((chunkBaseVy + localY) * mapWidth + (chunkBaseVx + localX));

                                if (mergedChanges.TryGetValue(globalVertexIndex, out var existing)) {
                                    existing.Merge(entry);
                                    mergedChanges[globalVertexIndex] = existing;
                                }
                                else {
                                    mergedChanges[globalVertexIndex] = entry;
                                }
                            }
                        }
                    }
                }
                catch (Exception) { }
            }

            return mergedChanges;
        }
    }
}
