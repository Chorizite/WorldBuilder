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
        /// The object tags from the game data (WeenieType, CreatureType, ItemType, etc.).
        /// </summary>
        [VectorStoreData]
        public string Tags { get; set; } = "";

        /// <summary>
        /// The object descriptions from the game data.
        /// </summary>
        [VectorStoreData]
        public string Descriptions { get; set; } = "";

        /// <summary>
        /// Combined embedding for the full record: "Name | Tags | Description".
        /// Encoding name, tags, and description jointly gives the model full context
        /// and produces better semantic matches than separate per-field vectors.
        /// 384 dimensions for bge-micro-v2.
        /// </summary>
        [VectorStoreVector(384)]
        public ReadOnlyMemory<float> Embedding { get; set; }
    }
}