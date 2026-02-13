using DatReaderWriter.Enums;
using System.Collections.Generic;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    /// <summary>
    /// Interface for tools that support texture selection.
    /// </summary>
    public interface ITexturePaintingTool : ILandscapeTool {
        /// <summary>The currently active document.</summary>
        WorldBuilder.Shared.Models.LandscapeDocument? ActiveDocument { get; }

        /// <summary>The currently selected texture.</summary>
        TerrainTextureType Texture { get; set; }

        /// <summary>All available textures for selection.</summary>
        IEnumerable<TerrainTextureType> AllTextures { get; }

        /// <summary>The currently selected scenery item.</summary>
        SceneryItem? SelectedScenery { get; set; }

        /// <summary>All available scenery for the currently selected texture.</summary>
        IEnumerable<SceneryItem> AllSceneries { get; }
    }

    /// <summary>
    /// Represents a scenery item for selection in the UI.
    /// </summary>
    public record SceneryItem(byte Index, string Name) {
        public string DisplayIndex => Index == 255 ? "-" : Index.ToString();
    }
}