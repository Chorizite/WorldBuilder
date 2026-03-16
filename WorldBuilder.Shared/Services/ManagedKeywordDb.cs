using System;

namespace WorldBuilder.Shared.Services {
    /// <summary>
    /// Information about a managed keyword database.
    /// </summary>
    public class ManagedKeywordDb {
        public Guid DatSetId { get; set; }
        public Guid AceDbId { get; set; }
        public int GeneratorVersion { get; set; }
        public DateTime LastGenerated { get; set; }
    }
}
