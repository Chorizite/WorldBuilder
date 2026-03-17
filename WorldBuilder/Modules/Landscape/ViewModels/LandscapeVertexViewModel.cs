using CommunityToolkit.Mvvm.ComponentModel;
using System.Numerics;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.Landscape.ViewModels;

public partial class LandscapeVertexViewModel : SelectedObjectViewModelBase {
    public override int VertexX => _vertexX;
    private int _vertexX;

    public override int VertexY => _vertexY;
    private int _vertexY;

    [ObservableProperty] private float _height;
    [ObservableProperty] private byte? _textureType;
    [ObservableProperty] private byte? _sceneryType;
    [ObservableProperty] private byte? _roadType;
    [ObservableProperty] private byte? _rawHeight;

    public string TextureName => TextureType.HasValue ? ((DatReaderWriter.Enums.TerrainTextureType)TextureType.Value).ToString() : "None";
    public string SceneryName { get; }
    public string VertexXHex => $"0x{VertexX:X4}";
    public string VertexYHex => $"0x{VertexY:X4}";

    public LandscapeVertexViewModel(int vx, int vy, LandscapeDocument doc, IDatReaderWriter dats, CommandHistory history) 
        : base(WorldBuilder.Shared.Models.ObjectId.Empty, 0, Vector3.Zero, Vector3.Zero, Quaternion.Identity, doc.BaseLayerId ?? "") {
        Type = ObjectType.Vertex;
        _vertexX = vx;
        _vertexY = vy;
        
        var region = doc.Region!;
        uint globalIndex = (uint)(vy * region.MapWidthInVertices + vx);
        InstanceId = WorldBuilder.Shared.Models.ObjectId.FromDat(ObjectType.Vertex, 0, globalIndex, 0);
        var entry = doc.GetCachedEntry(globalIndex);

        Height = doc.GetHeight(vx, vy);
        RawHeight = entry.Height;
        TextureType = entry.Type;
        SceneryType = entry.Scenery;
        RoadType = entry.Road;

        if (TextureType.HasValue && SceneryType.HasValue) {
            var sceneryId = region.GetSceneryId((int)TextureType.Value, SceneryType.Value);
            SceneryName = SceneryInfo.GetSceneryName(sceneryId);
        }
        else {
            SceneryName = "None";
        }

        float cellSize = region.CellSizeInUnits;
        int lbCellLen = region.LandblockCellLength;
        Vector2 mapOffset = region.MapOffset;

        int lbX = vx / lbCellLen;
        int lbY = vy / lbCellLen;
        int localVx = vx % lbCellLen;
        int localVy = vy % lbCellLen;

        float x = lbX * (cellSize * lbCellLen) + localVx * cellSize + mapOffset.X;
        float y = lbY * (cellSize * lbCellLen) + localVy * cellSize + mapOffset.Y;
        
        LandblockId = region.GetLandblockId(lbX, lbY);
        Position = new Vector3(x, y, Height);
        LocalPosition = new Vector3(localVx * cellSize, localVy * cellSize, Height);
    }
}
