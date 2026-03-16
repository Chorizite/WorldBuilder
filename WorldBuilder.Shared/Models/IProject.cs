using Microsoft.Extensions.DependencyInjection;
using WorldBuilder.Shared.Modules.Landscape;
using WorldBuilder.Shared.Repositories;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Models {
    /// <summary>
    /// Defines the interface for a WorldBuilder project.
    /// </summary>
    public interface IProject : IDisposable {
        /// <summary>
        /// Gets the name of the project.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the managed environment IDs for this project.
        /// </summary>
        ManagedEnvironmentIds ManagedIds { get; }

        /// <summary>
        /// Sets the managed ACE DB ID and persists it to the project database.
        /// </summary>
        Task SetManagedAceDbIdAsync(Guid? value);

        /// <summary>
        /// Raised when the ManagedAceDbId has changed.
        /// </summary>
        event EventHandler ManagedAceDbIdChanged;

        /// <summary>
        /// Gets a value indicating whether this project is read-only.
        /// </summary>
        bool IsReadOnly { get; }

        /// <summary>
        /// Gets the service provider for this project.
        /// </summary>
        ServiceProvider Services { get; }

        /// <summary>
        /// Gets the landscape module for this project.
        /// </summary>
        LandscapeModule Landscape { get; }
    }
}