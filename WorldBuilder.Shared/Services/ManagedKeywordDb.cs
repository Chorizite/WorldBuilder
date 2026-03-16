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

        public float KeywordProgress { get; set; }
        public float NameEmbeddingProgress { get; set; }
        public float DescEmbeddingProgress { get; set; }
        public bool IsComplete => KeywordProgress >= 1f && NameEmbeddingProgress >= 1f && DescEmbeddingProgress >= 1f;
    }
}
