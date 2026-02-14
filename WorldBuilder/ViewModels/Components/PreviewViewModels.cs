using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter;
using DatReaderWriter.Enums;
using DatReaderWriter.Lib.IO;
using System;
using WorldBuilder.ViewModels;

namespace WorldBuilder.ViewModels.Components;

public interface IPreviewViewModel {
}

public partial class TexturePreviewViewModel : ViewModelBase, IPreviewViewModel {
    [ObservableProperty] private TerrainTextureType _textureType;
    
    public TexturePreviewViewModel(TerrainTextureType textureType) {
        TextureType = textureType;
    }
}

public partial class SceneryPreviewViewModel : ViewModelBase, IPreviewViewModel {
    [ObservableProperty] private TerrainTextureType _textureType;
    [ObservableProperty] private byte _sceneryIndex;

    public SceneryPreviewViewModel(TerrainTextureType textureType, byte sceneryIndex) {
        TextureType = textureType;
        SceneryIndex = sceneryIndex;
    }
}

public partial class DataIdPreviewViewModel : ViewModelBase, IPreviewViewModel {
    [ObservableProperty] private uint _dataId;
    [ObservableProperty] private Type? _targetType;
    [ObservableProperty] private IDBObj? _dbObject;

    public DataIdPreviewViewModel(uint dataId, Type? targetType, IDBObj? dbObject = null) {
        DataId = dataId;
        TargetType = targetType ?? dbObject?.GetType();
        DbObject = dbObject;
    }
}
