using System;

namespace WorldBuilder.Shared.Modules.Landscape.Models
{
    /// <summary>
    /// Represents a serialized terrain patch as stored in the repository.
    /// </summary>
    public class TerrainPatch
    {
        /// <summary>The unique identifier for the terrain patch.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>The ID of the region this patch belongs to.</summary>
        public uint RegionId { get; set; }

        /// <summary>The serialized data for the terrain patch.</summary>
        public byte[]? Data { get; set; }

        /// <summary>The version of the terrain patch.</summary>
        public ulong Version { get; set; }

        /// <summary>The last modified timestamp.</summary>
        public DateTime LastModified { get; set; }
    }
}
