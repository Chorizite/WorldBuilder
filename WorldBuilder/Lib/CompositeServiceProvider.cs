using System;
using System.Collections.Generic;

namespace WorldBuilder.Lib {
    /// <summary>
    /// Provides a composite service provider that combines multiple service providers.
    /// </summary>
    public class CompositeServiceProvider() : IServiceProvider {
        private readonly List<IServiceProvider> _providers = [];

    /// <summary>
    /// Initializes a new instance of the CompositeServiceProvider class with the specified providers.
    /// </summary>
    /// <param name="providers">The service providers to combine</param>
    public CompositeServiceProvider(params IServiceProvider[] providers) : this() {
        _providers.AddRange(providers);
    }

    /// <summary>
    /// Gets a service of the specified type from the available providers.
    /// </summary>
    /// <param name="serviceType">The type of service to retrieve</param>
    /// <returns>The requested service if found in any provider, null otherwise</returns>
    public object? GetService(Type serviceType) {
        foreach (var provider in _providers) {
            var service = provider.GetService(serviceType);
            if (service != null) return service;
        }
        return null;
    }
}
}