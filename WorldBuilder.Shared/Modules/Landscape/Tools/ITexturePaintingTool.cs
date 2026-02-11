using DatReaderWriter.Enums;
using System.Collections.Generic;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    /// <summary>
    /// Interface for tools that support texture selection.
    /// </summary>
    public interface ITexturePaintingTool : ILandscapeTool {
        /// <summary>The currently selected texture.</summary>
        TerrainTextureType Texture { get; set; }

        /// <summary>All available textures for selection.</summary>
        IEnumerable<TerrainTextureType> AllTextures { get; }
    }
}