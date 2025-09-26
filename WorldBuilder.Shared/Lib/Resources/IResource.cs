using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;

namespace WorldBuilder.Shared.Lib.Resources {

    public abstract class IResource : IDisposable {
        protected class Rental : IEquatable<Rental> {
            public Type Type { get; }
            public uint Id { get; }
            public object ResourceObj { get; }
            public Rental(Type type, uint id, object resourceObj) {
                Type = type;
                Id = id;
                ResourceObj = resourceObj;
            }

            public bool Equals(Rental other) {
                return Type == other.Type && Id == other.Id;
            }
        }

        protected ResourceManager _resourceManager;
        protected List<Rental> _rentals = new List<Rental>();
        protected object _rentalLock = new object();

        public virtual uint Id { get; protected set; }

        public IResource(uint id, ResourceManager resourceManager) {
            Id = id;
            _resourceManager = resourceManager;
        }

        public IResource(ResourceManager resourceManager) : this(0, resourceManager) {
            _resourceManager = resourceManager;
        }

        protected virtual T? Rent<T>(uint resourceId) where T : IResource {
            var resource = _resourceManager.Rent<T>(resourceId);
            if (resource != null) {
                lock (_rentalLock) {
                    _rentals.Add(new Rental(typeof(T), resourceId, resource));
                }
            }
            return resource;
        }

        protected virtual void Release<T>(uint resourceId) where T : IResource {
            _resourceManager.Release<T>(resourceId);

            lock (_rentalLock) {
                var rental = _rentals.FirstOrDefault(r => r.Id == resourceId && r.ResourceObj.GetType() == typeof(T));
                if (rental != null) {
                    _rentals.Remove(rental);
                }
                else {
                    throw new Exception("Tried to release a null resource!");
                }
            }
        }

        public virtual void Dispose() {
            lock (_rentalLock) {
                foreach (var rental in _rentals) {
                    _resourceManager.Release(rental.Type, rental.Id);
                }
                _rentals.Clear();
            }
            GC.SuppressFinalize(this);
        }
    }
}