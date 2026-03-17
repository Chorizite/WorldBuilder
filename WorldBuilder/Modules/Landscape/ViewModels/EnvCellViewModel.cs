using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.DBObjs;
using System.Numerics;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.Landscape.ViewModels;

public partial class EnvCellViewModel : SelectedObjectViewModelBase {
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ObjectIdHex))]
    private uint _objectIdVal;

    [ObservableProperty] private uint _environmentId;

    public override uint ObjectId => ObjectIdVal;

    public string ObjectIdHex => $"0x{ObjectId:X8}";

    public EnvCellViewModel(uint objectId, ObjectId instanceId, ushort landblockId, Vector3 position, Vector3 localPosition, Quaternion rotation, IDatDatabase? cellDatabase, string layerId = "")
        : base(instanceId, landblockId, position, localPosition, rotation, layerId) {
        ObjectIdVal = objectId;
        Type = ObjectType.EnvCell;

        if (cellDatabase != null && cellDatabase.TryGet<EnvCell>(objectId, out var envCell)) {
            this.EnvironmentId = 0x0D000000u | (uint)envCell.EnvironmentId;
        }
    }
}
