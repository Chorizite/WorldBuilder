using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Numerics;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Landscape.ViewModels;

public abstract partial class SelectedObjectViewModelBase : ViewModelBase, ISelectedObjectInfo {
    [ObservableProperty] private ObjectType _type;
    ObjectType ISelectedObjectInfo.Type => Type;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstanceIdHex))]
    private ObjectId _instanceId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LandblockIdHex))]
    private ushort _landblockId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CellIdHex))]
    private uint? _cellId;

    [ObservableProperty] private string _layerId;

    [ObservableProperty] private Vector3 _position;
    [ObservableProperty] private Vector3 _localPosition;
    [ObservableProperty] private Quaternion _rotation;

    public virtual uint ObjectId => 0;

    public float X {
        get => LocalPosition.X;
        set {
            if (Math.Abs(LocalPosition.X - value) < 0.0001f) return;
            LocalPosition = new Vector3(value, LocalPosition.Y, LocalPosition.Z);
        }
    }
    public float Y {
        get => LocalPosition.Y;
        set {
            if (Math.Abs(LocalPosition.Y - value) < 0.0001f) return;
            LocalPosition = new Vector3(LocalPosition.X, value, LocalPosition.Z);
        }
    }
    public float Z {
        get => LocalPosition.Z;
        set {
            if (Math.Abs(LocalPosition.Z - value) < 0.0001f) return;
            LocalPosition = new Vector3(LocalPosition.X, LocalPosition.Y, value);
        }
    }

    public Vector3 RotationEuler => GeometryUtils.QuaternionToEuler(Rotation);
    public float RotationX {
        get => RotationEuler.X;
        set {
            if (Math.Abs(RotationEuler.X - value) < 0.0001f) return;
            Rotation = GeometryUtils.EulerToQuaternion(new Vector3(value, RotationEuler.Y, RotationEuler.Z));
        }
    }
    public float RotationY {
        get => RotationEuler.Y;
        set {
            if (Math.Abs(RotationEuler.Y - value) < 0.0001f) return;
            Rotation = GeometryUtils.EulerToQuaternion(new Vector3(RotationEuler.X, value, RotationEuler.Z));
        }
    }
    public float RotationZ {
        get => RotationEuler.Z;
        set {
            if (Math.Abs(RotationEuler.Z - value) < 0.0001f) return;
            Rotation = GeometryUtils.EulerToQuaternion(new Vector3(RotationEuler.X, RotationEuler.Y, value));
        }
    }

    public virtual int VertexX => 0;
    public virtual int VertexY => 0;

    public virtual SceneryDisqualificationReason DisqualificationReason { get; protected set; } = SceneryDisqualificationReason.None;

    public string InstanceIdHex => InstanceId.ToString();
    public string LandblockIdHex => $"0x{LandblockId:X4}";
    public virtual string CellIdHex => CellId.HasValue ? $"0x{CellId.Value:X8}" : "None";

    protected SelectedObjectViewModelBase(ObjectId instanceId, ushort landblockId, Vector3 position, Vector3 localPosition, Quaternion rotation, string layerId = "") {
        InstanceId = instanceId;
        LandblockId = landblockId;
        Position = position;
        LocalPosition = localPosition;
        Rotation = rotation;
        LayerId = layerId;
    }

    partial void OnLocalPositionChanged(Vector3 value) {
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        OnPropertyChanged(nameof(Z));
    }

    partial void OnRotationChanged(Quaternion value) {
        OnPropertyChanged(nameof(RotationEuler));
        OnPropertyChanged(nameof(RotationX));
        OnPropertyChanged(nameof(RotationY));
        OnPropertyChanged(nameof(RotationZ));
    }
}
