using DatReaderWriter.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorldBuilder.Shared.Lib.Resources {
    public class DatFile<T> : IDatResource<T> where T : DBObj, new() {
        private IDatReaderWriter _datReader;

        public DatFile(uint id, IDatReaderWriter datReader, ResourceManager resourceManager) : base(id, resourceManager) {
            _datReader = datReader;
        }

        protected override Task<bool> LoadInternal() {
            if (_datReader.TryGet(Id, out T file)) {
                File = file;
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        public override void Dispose() {
            //_datReader.ClearCache<T>(Id);
            base.Dispose();
        }
    }
}
