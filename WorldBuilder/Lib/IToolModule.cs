using WorldBuilder.ViewModels;

namespace WorldBuilder.Lib;

/// <summary>
/// Interface for tool modules that can be displayed as tabs in the main application.
/// </summary>
public interface IToolModule {
    /// <summary>
    /// Gets the display name of the tool.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the view model for the tool.
    /// </summary>
    ViewModelBase ViewModel { get; }
}
