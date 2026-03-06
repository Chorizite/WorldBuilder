using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Repositories;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Modules.Landscape.Services {
    /// <summary>
    /// Implements the unified landscape data provider.
    /// </summary>
    public class LandscapeDataProvider : ILandscapeDataProvider {
        private readonly IProjectRepository _repo;

        public LandscapeDataProvider(IProjectRepository repo) {
            _repo = repo;
        }

        /// <inheritdoc/>
        public async Task<MergedLandblock> GetMergedLandblockAsync(uint landblockId, IDatDatabase? cellDatabase, IEnumerable<string> visibleLayerIds, string? baseLayerId, CancellationToken ct) {
            var merged = new MergedLandblock();
            var visibleLayers = new HashSet<string>(visibleLayerIds);
            var effectiveBaseLayerId = baseLayerId ?? "Base";

            // 1. Parse base from DAT
            var lbFileId = (landblockId & 0xFFFF0000) | 0xFFFE;

            if (cellDatabase != null) {
                if (cellDatabase.TryGet<LandBlockInfo>(lbFileId, out var lbi) && lbi != null) {
                    for (int i = 0; i < (lbi.Objects?.Count ?? 0); i++) {
                        var stab = lbi.Objects![i];
                        if (stab == null) continue;

                        ulong instanceId = InstanceIdConstants.EncodeStaticObject(lbFileId, (ushort)i);
                        var pos = new float[7];
                        if (stab.Frame != null) {
                            pos[0] = stab.Frame.Origin.X;
                            pos[1] = stab.Frame.Origin.Y;
                            pos[2] = stab.Frame.Origin.Z;
                            pos[3] = stab.Frame.Orientation.W;
                            pos[4] = stab.Frame.Orientation.X;
                            pos[5] = stab.Frame.Orientation.Y;
                            pos[6] = stab.Frame.Orientation.Z;
                        }

                        merged.StaticObjects[instanceId] = new StaticObject {
                            SetupId = stab.Id,
                            Position = pos,
                            InstanceId = instanceId,
                            LayerId = effectiveBaseLayerId
                        };
                    }

                    for (int i = 0; i < (lbi.Buildings?.Count ?? 0); i++) {
                        var bldg = lbi.Buildings![i];
                        if (bldg == null) continue;

                        ulong instanceId = InstanceIdConstants.EncodeBuilding(lbFileId, (ushort)i);
                        var pos = new float[7];
                        if (bldg.Frame != null) {
                            pos[0] = bldg.Frame.Origin.X;
                            pos[1] = bldg.Frame.Origin.Y;
                            pos[2] = bldg.Frame.Origin.Z;
                            pos[3] = bldg.Frame.Orientation.W;
                            pos[4] = bldg.Frame.Orientation.X;
                            pos[5] = bldg.Frame.Orientation.Y;
                            pos[6] = bldg.Frame.Orientation.Z;
                        }

                        merged.Buildings[instanceId] = new BuildingObject {
                            ModelId = bldg.ModelId,
                            Position = pos,
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
        public async Task<Cell> GetMergedEnvCellAsync(uint cellId, IDatDatabase? cellDatabase, IEnumerable<string> visibleLayerIds, string? baseLayerId, CancellationToken ct) {
            var properties = new Cell();
            var visibleLayers = new HashSet<string>(visibleLayerIds);
            var effectiveBaseLayerId = baseLayerId ?? "Base";

            if (cellDatabase != null && cellDatabase.TryGet<EnvCell>(cellId, out var cell)) {
                properties = new Cell {
                    EnvironmentId = cell.EnvironmentId,
                    Flags = (uint)cell.Flags,
                    CellStructure = cell.CellStructure,
                    Position = [cell.Position.Origin.X, cell.Position.Origin.Y, cell.Position.Origin.Z, cell.Position.Orientation.W, cell.Position.Orientation.X, cell.Position.Orientation.Y, cell.Position.Orientation.Z],
                    Surfaces = new List<ushort>(cell.Surfaces),
                    Portals = cell.CellPortals.Select(p => new WbCellPortal(p)).ToList(),
                    LayerId = effectiveBaseLayerId
                };

                if (cell.StaticObjects != null) {
                    for (int i = 0; i < cell.StaticObjects.Count; i++) {
                        var stab = cell.StaticObjects[i];
                        if (stab == null) continue;

                        ulong instanceId = InstanceIdConstants.EncodeEnvCellStaticObject(cellId, (ushort)i, false);
                        var pos = new float[7];
                        if (stab.Frame != null) {
                            pos[0] = stab.Frame.Origin.X;
                            pos[1] = stab.Frame.Origin.Y;
                            pos[2] = stab.Frame.Origin.Z;
                            pos[3] = stab.Frame.Orientation.W;
                            pos[4] = stab.Frame.Orientation.X;
                            pos[5] = stab.Frame.Orientation.Y;
                            pos[6] = stab.Frame.Orientation.Z;
                        }

                        properties.StaticObjects[instanceId] = new StaticObject {
                            SetupId = stab.Id,
                            Position = pos,
                            InstanceId = instanceId,
                            LayerId = effectiveBaseLayerId
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
                    StaticObjects = properties.StaticObjects
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
    }
}