using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Lib.IO;
using DatReaderWriter.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
namespace WorldBuilder.Shared.Lib {
    public class DefaultDatReaderWriter : IDatReaderWriter {
        private object _lock = new object();
        public DatCollection Dats { get; }

        public DefaultDatReaderWriter(string datPath, DatAccessType accessType) {
            Dats = new DatCollection(new DatCollectionOptions() {
                AccessType = accessType,
                DatDirectory = datPath,
                FileCachingStrategy = FileCachingStrategy.Never,
                IndexCachingStrategy = IndexCachingStrategy.Never
            });
        }

        public bool TryGet<T>(uint id, [MaybeNullWhen(false)] out T file) where T : IDBObj, new() {
            lock (_lock) {
                return typeof(T) switch {
                    Type _ when typeof(T) == typeof(LandBlock) => Dats.Cell.TryGet(id, out file),
                    Type _ when typeof(T) == typeof(Region) => Dats.Portal.TryGet(id, out file),
                    Type _ when typeof(T) == typeof(RenderTexture) => Dats.Portal.TryGet(id, out file),
                    Type _ when typeof(T) == typeof(RenderSurface) => Dats.Portal.TryGet(id, out file),
                    Type _ when typeof(T) == typeof(SurfaceTexture) => Dats.Portal.TryGet(id, out file),
                    _ => throw new NotImplementedException($"DefaultDatReaderWriter does not currently support {typeof(T)}"),
                };
            }
        }

        public bool TrySave<T>(T file, int? iteration = 0) where T : IDBObj, new() {
            lock (_lock) {
                return typeof(T) switch {
                    Type _ when typeof(T) == typeof(LandBlock) => Dats.Cell.TryWriteFile(file, iteration),
                    Type _ when typeof(T) == typeof(Region) => Dats.Portal.TryWriteFile(file, iteration),
                    Type _ when typeof(T) == typeof(RenderTexture) => Dats.Portal.TryWriteFile(file, iteration),
                    Type _ when typeof(T) == typeof(RenderSurface) => Dats.Portal.TryWriteFile(file, iteration),
                    Type _ when typeof(T) == typeof(SurfaceTexture) => Dats.Portal.TryWriteFile(file, iteration),
                    _ => throw new NotImplementedException($"DefaultDatReaderWriter does not currently support {typeof(T)}"),
                };
            }
        }

        public void Dispose() {
            Dats.Dispose();
        }
    }
}
