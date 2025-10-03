using System;
using System.Collections.Generic;

namespace WorldBuilder.Lib {
    public class CompositeServiceProvider() : IServiceProvider {
        private readonly List<IServiceProvider> _providers = [];

        public CompositeServiceProvider(params IServiceProvider[] providers) : this() {
            _providers.AddRange(providers);
        }

        public object? GetService(Type serviceType) {
            foreach (var provider in _providers) {
                var service = provider.GetService(serviceType);
                if (service != null) return service;
            }
            return null;
        }
    }
}
