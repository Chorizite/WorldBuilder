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
        public async Task<MergedLandblock> GetMergedLandblockAsync(uint landblockId, IDatDatabase? cellDatabase, IEnumerable<string> visibleLayerIds, string? baseLayerId, CancellationToken ct) {
            var merged = new MergedLandblock();
            var visibleLayers = new HashSet<string>(visibleLayerIds);
            var effectiveBaseLayerId = baseLayerId ?? string.Empty;

            // 1. Parse base from DAT
            var lbFileId = (landblockId & 0xFFFF0000) | 0xFFFE;

            if (cellDatabase != null) {
                if (cellDatabase.TryGet<LandBlockInfo>(lbFileId, out var lbi) && lbi != null) {
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

            return merged;
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyDictionary<uint, MergedLandblock>> GetMergedLandblocksAsync(IEnumerable<uint> landblockIds, IDatDatabase? cellDatabase, IEnumerable<string> visibleLayerIds, string? baseLayerId, CancellationToken ct) {
            var results = new Dictionary<uint, MergedLandblock>();
            var ids = landblockIds.ToList();
            if (ids.Count == 0) return results;

            var visibleLayers = new HashSet<string>(visibleLayerIds);
            var effectiveBaseLayerId = baseLayerId ?? string.Empty;

            // 1. Parse base from DAT for all landblocks
            foreach (var landblockId in ids) {
                var merged = new MergedLandblock();
                results[landblockId] = merged;

                var lbFileId = (landblockId & 0xFFFF0000) | 0xFFFE;

                if (cellDatabase != null) {
                    if (cellDatabase.TryGet<LandBlockInfo>(lbFileId, out var lbi) && lbi != null) {
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

            return results;
        }

        /// <inheritdoc/>
        public async Task<Cell> GetMergedEnvCellAsync(uint cellId, IDatDatabase? cellDatabase, IEnumerable<string> visibleLayerIds, string? baseLayerId, CancellationToken ct) {
            var properties = new Cell();
            var visibleLayers = new HashSet<string>(visibleLayerIds);
            var effectiveBaseLayerId = baseLayerId ?? string.Empty;

            if (cellDatabase != null && cellDatabase.TryGet<EnvCell>(cellId, out var cell)) {
                properties = new Cell {
                    EnvironmentId = cell.EnvironmentId,
                    Flags = (uint)cell.Flags,
                    CellStructure = cell.CellStructure,
                    Position = cell.Position.Origin,
                    Rotation = cell.Position.Orientation,
                    Surfaces = new List<ushort>(cell.Surfaces),
                    Portals = cell.CellPortals.Select(p => new WbCellPortal(p)).ToList(),
                    LayerId = effectiveBaseLayerId,
                    CellId = cellId
                };

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
                    Surfaces = repoCell.Surfaces,
                    Portals = repoCell.Portals,
                    LayerId = repoCell.LayerId,
                    StaticObjects = properties.StaticObjects,
                    CellId = repoCell.CellId
                };
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
            if (patches == null) return mergedChanges;

            int mapWidth = regionInfo.MapWidthInVertices;
            int vertexStride = regionInfo.LandblockVerticeLength;
            int strideMinusOne = vertexStride - 1;

            foreach (var patch in patches) {
                if (patch.Data == null || patch.Data.Length == 0) continue;

                try {
                    var doc = MemoryPackSerializer.Deserialize<TerrainPatchDocument>(patch.Data);
                    if (doc == null) continue;

                    // Extract chunk coords from ID: TerrainPatch_{regionId}_{chunkX}_{chunkY}
                    var parts = doc.Id.Split('_');
                    if (parts.Length < 4) continue;
                    if (!uint.TryParse(parts[2], out var chunkX) || !uint.TryParse(parts[3], out var chunkY)) continue;

                    // Calculate base offsets for this chunk
                    int chunkBaseVx = (int)(chunkX * LandscapeChunk.LandblocksPerChunk * strideMinusOne);
                    int chunkBaseVy = (int)(chunkY * LandscapeChunk.LandblocksPerChunk * strideMinusOne);

                    foreach (var layerId in visibleLayers) {
                        if (doc.LayerEdits.TryGetValue(layerId, out var layerVertices)) {
                            for (int i = 0; i < layerVertices.Length; i++) {
                                var entry = layerVertices[i];
                                if (entry.Flags == TerrainEntryFlags.None) continue;

                                // Calculate local chunk coords (65x65)
                                int localY = i / LandscapeChunk.ChunkVertexStride;
                                int localX = i % LandscapeChunk.ChunkVertexStride;

                                // Calculate global vertex index
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
                catch (Exception ex) {
                    _log?.LogError(ex, "Error deserializing terrain patch {PatchId}", patch.Id);
                }
            }

            return mergedChanges;
        }
    }
}
