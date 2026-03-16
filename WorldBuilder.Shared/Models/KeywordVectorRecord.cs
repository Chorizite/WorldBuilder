using System;
using Microsoft.Extensions.VectorData;

namespace WorldBuilder.Shared.Models {
    /// <summary>
    /// Represents a vector record for storing setup-related embeddings and search data.
    /// </summary>
    public class KeywordVectorRecord {
        /// <summary>
        /// Primary key for the record (SetupId).
        /// </summary>
        [VectorStoreKey]
        public int SetupId { get; set; }

        /// <summary>
        /// The object names from the game data.
        /// </summary>
        [VectorStoreData]
        public string Names { get; set; } = "";

        /// <summary>
        /// The object descriptions from the game data.
        /// </summary>
        [VectorStoreData]
        public string Descriptions { get; set; } = "";

        /// <summary>
        /// The embedding vector for names (384 dimensions for bge-micro-v2).
        /// </summary>
        [VectorStoreVector(384)]
        public ReadOnlyMemory<float> NameEmbedding { get; set; }

        /// <summary>
        /// The embedding vector for descriptions (384 dimensions for bge-micro-v2).
        /// </summary>
        [VectorStoreVector(384)]
        public ReadOnlyMemory<float> DescEmbedding { get; set; }
    }
}
