using DatReaderWriter.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorldBuilder.Shared.Lib.Resources {

    public abstract class IDatResource<T> : IModelResource where T : DBObj, new() {
        public T? File { get; protected set; }

        public IDatResource(uint id, ResourceManager resourceManager) : base(id, resourceManager) {

        }
    }
}
