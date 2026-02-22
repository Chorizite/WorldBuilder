using DatReaderWriter;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Models;

public partial class LandscapeDocument {
    /// <summary>
    /// Adds a static object to a landscape layer.
    /// </summary>
    public async Task<Result<bool>> AddStaticObjectAsync(string layerId, uint landblockId, StaticObject obj, IDatReaderWriter dats, IDocumentManager documentManager, ITransaction tx, CancellationToken ct) {
        try {
            var layer = FindItem(layerId) as LandscapeLayer;
            if (layer == null) {
                return Result<bool>.Failure(Error.NotFound($"Layer not found: {layerId}"));
            }

            ushort chunkId = _coords.GetChunkIdForLandblock(landblockId);
            await GetOrLoadChunkAsync(chunkId, dats, documentManager, ct);

            // If we're adding it back (e.g. undoing a delete), make sure it's not in DeletedInstanceIds
            if (LoadedChunks.TryGetValue(chunkId, out var chunk) && chunk.Edits != null) {
                if (chunk.Edits.LayerEdits.TryGetValue(layerId, out var layerEdits)) {
                    layerEdits.DeletedInstanceIds.Remove(obj.InstanceId);
                }
            }

            AddStaticObject(layerId, landblockId, obj);

            Version++;
            NotifyLandblockChanged([( (int)(landblockId >> 24), (int)((landblockId >> 16) & 0xFF) )]);

            if (LoadedChunks.TryGetValue(chunkId, out var chunk2)) {
                var result = await PersistChunkEditsAsync(chunk2, documentManager, tx, ct);
                if (result.IsFailure) return result;
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex) {
            return Result<bool>.Failure(Error.Failure(ex.Message));
        }
    }

    /// <summary>
    /// Deletes a static object from a landscape layer.
    /// </summary>
    public async Task<Result<bool>> DeleteStaticObjectAsync(string layerId, uint landblockId, uint instanceId, IDatReaderWriter dats, IDocumentManager documentManager, ITransaction tx, CancellationToken ct) {
        try {
            var layer = FindItem(layerId) as LandscapeLayer;
            if (layer == null) {
                return Result<bool>.Failure(Error.NotFound($"Layer not found: {layerId}"));
            }

            ushort chunkId = _coords.GetChunkIdForLandblock(landblockId);
            await GetOrLoadChunkAsync(chunkId, dats, documentManager, ct);

            if (LoadedChunks.TryGetValue(chunkId, out var chunk) && chunk.Edits != null) {
                if (!chunk.Edits.LayerEdits.TryGetValue(layerId, out var layerEdits)) {
                    layerEdits = new LandscapeChunkEdits();
                    chunk.Edits.LayerEdits[layerId] = layerEdits;
                }

                // If it's owned by this layer, remove it from the owned list
                if (layerEdits.ExteriorStaticObjects.TryGetValue(landblockId, out var list)) {
                    list.RemoveAll(x => x.InstanceId == instanceId);
                }

                // Always add to DeletedInstanceIds to hide it from lower layers/base
                if (!layerEdits.DeletedInstanceIds.Contains(instanceId)) {
                    layerEdits.DeletedInstanceIds.Add(instanceId);
                }
                
                chunk.Edits.Version++;
            }

            Version++;
            NotifyLandblockChanged([( (int)(landblockId >> 24), (int)((landblockId >> 16) & 0xFF) )]);

            if (LoadedChunks.TryGetValue(chunkId, out var chunk2)) {
                var result = await PersistChunkEditsAsync(chunk2, documentManager, tx, ct);
                if (result.IsFailure) return result;
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex) {
            return Result<bool>.Failure(Error.Failure(ex.Message));
        }
    }

    /// <summary>
    /// Updates a static object in a landscape layer (handles moves between landblocks).
    /// </summary>
    public async Task<Result<bool>> UpdateStaticObjectAsync(string layerId, uint oldLandblockId, uint newLandblockId, StaticObject newObj, IDatReaderWriter dats, IDocumentManager documentManager, ITransaction tx, CancellationToken ct) {
        try {
            var layer = FindItem(layerId) as LandscapeLayer;
            if (layer == null) {
                return Result<bool>.Failure(Error.NotFound($"Layer not found: {layerId}"));
            }

            ushort oldChunkId = _coords.GetChunkIdForLandblock(oldLandblockId);
            ushort newChunkId = _coords.GetChunkIdForLandblock(newLandblockId);

            await GetOrLoadChunkAsync(oldChunkId, dats, documentManager, ct);
            if (newChunkId != oldChunkId) {
                await GetOrLoadChunkAsync(newChunkId, dats, documentManager, ct);
            }

            // 1. Remove from old landblock in this layer (if it's there)
            if (LoadedChunks.TryGetValue(oldChunkId, out var oldChunk) && oldChunk.Edits != null) {
                if (oldChunk.Edits.LayerEdits.TryGetValue(layerId, out var layerEdits)) {
                    if (layerEdits.ExteriorStaticObjects.TryGetValue(oldLandblockId, out var list)) {
                        list.RemoveAll(x => x.InstanceId == newObj.InstanceId);
                    }
                }
                oldChunk.Edits.Version++;
            }

            // 2. Add to new landblock
            AddStaticObject(layerId, newLandblockId, newObj);

            // 3. Mark as hidden from lower layers if it was a modification of an existing object
            // Actually, we should just make sure it stays in DeletedInstanceIds if we want to override lower layers.
            // If it's a move of an object that existed in base, it should have a tombstone in our layer.
            if (LoadedChunks.TryGetValue(newChunkId, out var newChunk) && newChunk.Edits != null) {
                if (newChunk.Edits.LayerEdits.TryGetValue(layerId, out var layerEdits)) {
                    if (!layerEdits.DeletedInstanceIds.Contains(newObj.InstanceId)) {
                        layerEdits.DeletedInstanceIds.Add(newObj.InstanceId);
                    }
                }
                newChunk.Edits.Version++;
            }

            Version++;
            var affectedLandblocks = new HashSet<(int, int)> {
                ((int)(oldLandblockId >> 24), (int)((oldLandblockId >> 16) & 0xFF)),
                ((int)(newLandblockId >> 24), (int)((newLandblockId >> 16) & 0xFF))
            };
            NotifyLandblockChanged(affectedLandblocks);

            if (LoadedChunks.TryGetValue(oldChunkId, out var chunk1)) {
                var result = await PersistChunkEditsAsync(chunk1, documentManager, tx, ct);
                if (result.IsFailure) return result;
            }
            if (newChunkId != oldChunkId && LoadedChunks.TryGetValue(newChunkId, out var chunk2)) {
                var result = await PersistChunkEditsAsync(chunk2, documentManager, tx, ct);
                if (result.IsFailure) return result;
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex) {
            return Result<bool>.Failure(Error.Failure(ex.Message));
        }
    }

    private async Task<Result<bool>> PersistChunkEditsAsync(LandscapeChunk chunk, IDocumentManager documentManager, ITransaction tx, CancellationToken ct) {
        if (chunk.EditsRental != null) {
            var chunkPersistResult = await documentManager.PersistDocumentAsync(chunk.EditsRental, tx, ct);
            if (chunkPersistResult.IsFailure) {
                return Result<bool>.Failure(chunkPersistResult.Error);
            }
        }
        else if (chunk.EditsDetached != null) {
            var createResult = await documentManager.CreateDocumentAsync(chunk.EditsDetached, tx, ct);
            if (createResult.IsFailure) {
                return Result<bool>.Failure(createResult.Error);
            }
            chunk.EditsRental = createResult.Value;
            chunk.EditsDetached = null;
        }
        return Result<bool>.Success(true);
    }
}
