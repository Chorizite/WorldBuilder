using DatReaderWriter;
using DatReaderWriter.Lib.IO;

namespace WorldBuilder.Shared.Lib {
    public interface IDatReaderWriter : IDisposable {
        //public DatCollection Dats { get; }
        bool TryGet<T>(uint id, out T file) where T : IDBObj, new();
        bool TrySave<T>(T file, int? iteration = 0) where T : IDBObj, new();
    }
}