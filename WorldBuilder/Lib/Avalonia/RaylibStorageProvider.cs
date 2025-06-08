using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Environment = System.Environment;

namespace WorldBuilder.Lib.Avalonia;

internal sealed class RaylibStorageProvider : IStorageProvider {
    public bool CanOpen => false;

    public bool CanSave => false;
    public bool CanPickFolder => false;

    public Task<IStorageBookmarkFile?> OpenFileBookmarkAsync(string bookmark) {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<IStorageFile>> OpenFilePickerAsync(FilePickerOpenOptions options) {
        throw new NotImplementedException();
    }

    public Task<IStorageBookmarkFolder?> OpenFolderBookmarkAsync(string bookmark) {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<IStorageFolder>> OpenFolderPickerAsync(FolderPickerOpenOptions options) {
        throw new NotImplementedException();
    }

    public Task<IStorageFile?> SaveFilePickerAsync(FilePickerSaveOptions options) {
        throw new NotImplementedException();
    }

    public Task<IStorageFile?> TryGetFileFromPathAsync(Uri filePath) {
        throw new NotImplementedException();
    }

    public Task<IStorageFolder?> TryGetFolderFromPathAsync(Uri folderPath) {
        throw new NotImplementedException();
    }

    public Task<IStorageFolder?> TryGetWellKnownFolderAsync(WellKnownFolder wellKnownFolder) {
        throw new NotImplementedException();
    }
}
