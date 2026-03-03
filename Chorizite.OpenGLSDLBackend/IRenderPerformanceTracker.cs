namespace Chorizite.OpenGLSDLBackend;

/// <summary>
/// Interface for tracking render performance metrics.
/// </summary>
public interface IRenderPerformanceTracker {
    /// <summary>
    /// Gets or sets the time spent in the prepare phase (milliseconds).
    /// </summary>
    double PrepareTime { get; set; }

    /// <summary>
    /// Gets or sets the time spent in the opaque render phase (milliseconds).
    /// </summary>
    double OpaqueTime { get; set; }

    /// <summary>
    /// Gets or sets the time spent in the transparent render phase (milliseconds).
    /// </summary>
    double TransparentTime { get; set; }

    /// <summary>
    /// Gets or sets the time spent in the debug render phase (milliseconds).
    /// </summary>
    double DebugTime { get; set; }
}
