using CommunityToolkit.Mvvm.ComponentModel;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;

using System.Numerics;

namespace WorldBuilder.Modules.Landscape.ViewModels;

public partial class LandscapeVertexViewModel : ViewModelBase, ISelectedObjectInfo {
    public InspectorSelectionType Type => InspectorSelectionType.Vertex;
    public uint LandblockId { get; }
    public ulong InstanceId => 0;
    public ushort SecondaryId => 0;
    public uint ObjectId => 0;
    public Vector3 Position { get; }
    public Quaternion Rotation => Quaternion.Identity;

    [ObservableProperty] private int _vertexX;
    [ObservableProperty] private int _vertexY;
    [ObservableProperty] private float _height;
    [ObservableProperty] private byte? _textureType;
    [ObservableProperty] private byte? _sceneryType;
    [ObservableProperty] private byte? _roadType;
    [ObservableProperty] private byte? _rawHeight;
    
    public string TextureName => TextureType.HasValue ? ((DatReaderWriter.Enums.TerrainTextureType)TextureType.Value).ToString() : "None";
    public string SceneryName { get; }
    public string VertexXHex => $"0x{VertexX:X4}";
    public string VertexYHex => $"0x{VertexY:X4}";

    public LandscapeVertexViewModel(int vx, int vy, LandscapeDocument doc, IDatReaderWriter dats, CommandHistory history) {
        VertexX = vx;
        VertexY = vy;
        
        var region = doc.Region!;
        uint globalIndex = (uint)(vy * region.MapWidthInVertices + vx);
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
        Position = new Vector3(x, y, Height);
        LandblockId = region.GetLandblockId(lbX, lbY);
    }
}
