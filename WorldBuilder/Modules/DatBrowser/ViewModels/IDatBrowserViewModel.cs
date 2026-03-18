using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Lib.IO;
using DatReaderWriter.Types;
using DatReaderWriter;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public interface IDatBrowserViewModel : IKeywordSearchViewModel {
        IDBObj? SelectedObject { get; }
        uint SelectedFileId { get; set; }
        new GridBrowserViewModel GridBrowser { get; }
    }
}
