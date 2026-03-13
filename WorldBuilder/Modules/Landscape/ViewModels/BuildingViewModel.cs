using CommunityToolkit.Mvvm.ComponentModel;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Modules.Landscape.ViewModels;

public partial class BuildingViewModel : SelectedObjectViewModelBase {
    public override ObjectType Type => ObjectType.Building;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModelIdHex))]
    private uint _modelId;

    public override uint ObjectId => ModelId;

    public string ModelIdHex => $"0x{ModelId:X8}";

    public BuildingViewModel(uint modelId, ObjectId instanceId, ushort landblockId, Vector3 position, Vector3 localPosition, Quaternion rotation) 
        : base(instanceId, landblockId, position, localPosition, rotation) {
        ModelId = modelId;
    }
}
