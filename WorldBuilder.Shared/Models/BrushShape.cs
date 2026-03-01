namespace WorldBuilder.Shared.Models;

/// <summary>
/// The shape of the terrain editor brush.
/// </summary>
public enum BrushShape {
    /// <summary>
    /// A circular brush.
    /// </summary>
    Circle = 0,

    /// <summary>
    /// A square/rounded box brush.
    /// </summary>
    Square = 1,

    /// <summary>
    /// A crosshair brush.
    /// </summary>
    Crosshair = 2
}
