using System.Numerics;
using System.ComponentModel;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    /// <summary>
    /// Defines a brush used for landscape editing.
    /// </summary>
    public interface ILandscapeBrush : INotifyPropertyChanged {
        /// <summary>Whether the brush is currently visible.</summary>
        bool IsVisible { get; set; }

        /// <summary>The world position of the brush center.</summary>
        Vector3 Position { get; set; }

        /// <summary>The radius of the brush in world units.</summary>
        float Radius { get; set; }

        /// <summary>The shape of the brush.</summary>
        BrushShape Shape { get; set; }
    }
}
