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
    public uint LandblockId => 0;
    public uint InstanceId => 0;
    public uint ObjectId => 0;
    public Vector3 Position => Vector3.Zero;
    public Quaternion Rotation => Quaternion.Identity;

    [ObservableProperty] private int _vertexX;
    [ObservableProperty] private int _vertexY;
    [ObservableProperty] private float _height;
    [ObservableProperty] private byte? _textureType;
    [ObservableProperty] private byte? _sceneryType;
    
    public string VertexXHex => $"0x{VertexX:X4}";
    public string VertexYHex => $"0x{VertexY:X4}";

    public LandscapeVertexViewModel(int vx, int vy, LandscapeDocument doc, IDatReaderWriter dats, CommandHistory history) {
        VertexX = vx;
        VertexY = vy;
        
        uint globalIndex = (uint)(vy * doc.Region!.MapWidthInVertices + vx);
        var entry = doc.GetCachedEntry(globalIndex);

        Height = doc.GetHeight(vx, vy);
        TextureType = entry.Type;
        SceneryType = entry.Scenery;
    }
}
