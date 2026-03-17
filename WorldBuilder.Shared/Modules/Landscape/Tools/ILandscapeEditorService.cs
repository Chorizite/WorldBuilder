using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools;

public interface ILandscapeEditorService {
    void RequestSave(string docId, IEnumerable<ushort>? affectedChunks = null);
    void InvalidateLandblock(int x, int y);
    void UpdateStaticObject(string layerId, ushort oldLbId, StaticObject oldObject, ushort newLbId, StaticObject newObj);
    void AddStaticObject(string layerId, ushort landblockId, StaticObject obj);
    void DeleteStaticObject(string layerId, ushort landblockId, StaticObject obj);
    void NotifyObjectPositionPreview(ushort landblockId, ObjectId instanceId, Vector3 position, Quaternion rotation, uint currentCellId, uint modelId = 0);
    
    BoundingBox? GetStaticObjectBounds(ushort landblockId, ObjectId instanceId);
    BoundingBox? GetStaticObjectLocalBounds(ushort landblockId, ObjectId instanceId);
    BoundingBox? GetModelBounds(uint modelId);
    (Vector3 position, Quaternion rotation, Vector3 localPosition)? GetStaticObjectTransform(ushort landblockId, ObjectId instanceId);
    uint GetEnvCellAt(Vector3 worldPos);
}
