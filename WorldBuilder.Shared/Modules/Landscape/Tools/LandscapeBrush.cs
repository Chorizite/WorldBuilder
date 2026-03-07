using CommunityToolkit.Mvvm.ComponentModel;
using System.Numerics;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    /// <summary>
    /// Default implementation of a landscape brush.
    /// </summary>
    public partial class LandscapeBrush : ObservableObject, ILandscapeBrush {
        [ObservableProperty] private bool _isVisible;
        [ObservableProperty] private Vector3 _position;
        [ObservableProperty] private float _radius = 30f;
        [ObservableProperty] private BrushShape _shape = BrushShape.Circle;
    }
}
