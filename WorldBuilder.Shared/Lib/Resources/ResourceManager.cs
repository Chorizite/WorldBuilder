using System;
using System.Collections.Concurrent;
using System.Reflection.Metadata;

namespace WorldBuilder.Shared.Lib.Resources {
    public class ResourceManager : IDisposable {
        internal ConcurrentDictionary<Type, ConcurrentDictionary<uint, IResource>> _resourceCache = new ConcurrentDictionary<Type, ConcurrentDictionary<uint, IResource>>();

        internal ConcurrentDictionary<Type, ConcurrentDictionary<uint, int>> _resourceReferences = new ConcurrentDictionary<Type, ConcurrentDictionary<uint, int>>();

        internal ConcurrentDictionary<Type, object> _resourceTypeLocks = new ConcurrentDictionary<Type, object>();

        private object _lock = new object();

        public T? Rent<T>(uint id) where T : IResource {
            lock (_lock) {
                var t = typeof(T);
                if (!_resourceCache.TryGetValue(typeof(T), out var resources)) {
                    resources = new ConcurrentDictionary<uint, IResource>();
                    if (!_resourceCache.TryAdd(typeof(T), resources)) {
                        throw new Exception("Failed to add resource type!");
                    }
                }
                if (!_resourceReferences.TryGetValue(typeof(T), out var referenceCounts)) {
                    referenceCounts = new ConcurrentDictionary<uint, int>();
                    if (!_resourceReferences.TryAdd(typeof(T), referenceCounts)) {
                        throw new Exception("Failed to add resource type reference count!");
                    }
                }
                if (!_resourceTypeLocks.TryGetValue(typeof(T), out var referenceTypeLock)) {
                    referenceTypeLock = new object();
                    if (!_resourceTypeLocks.TryAdd(typeof(T), referenceTypeLock)) {
                        throw new Exception("Failed to add resource type lock!");
                    }
                }

                if (!resources.TryGetValue(id, out var obj)) {
                    obj = Activator.CreateInstance(typeof(T), id, this) as T;
                    if (obj == null || !resources.TryAdd(id, obj)) {
                        throw new Exception("Failed to add resource!");
                    }

                }

                if (obj == null) {
                    return null;
                }

                lock (referenceTypeLock) {
                    if (!referenceCounts.TryGetValue(id, out var refCount)) {
                        if (!referenceCounts.TryAdd(id, 1)) {
                            throw new Exception("Failed to add resource reference count!");
                        }
                    }
                    else {
                        if (!referenceCounts.TryUpdate(id, refCount + 1, refCount)) {
                            throw new Exception("Failed to update resource reference count!");
                        }
                    }
                }

                return obj as T;
            }
        }

        public void Release<T>(T resource) where T : IResource {
            Release(typeof(T), resource.Id);
        }


        public void Release<T>(uint id) where T : IResource {
            Release(typeof(T), id);
        }

        public void Release(Type type, uint id) {
            lock (_lock) {
                if (_resourceReferences.TryGetValue(type, out var referenceCounts)) {
                    if (_resourceTypeLocks.TryGetValue(type, out var refLock)) {
                        int referenceCount = 0;
                        lock (refLock) {
                            if (referenceCounts.TryGetValue(id, out referenceCount)) {
                                if (!referenceCounts.TryUpdate(id, referenceCount - 1, referenceCount)) {
                                    throw new Exception($"Failed to update resource reference count!");
                                }
                            }
                            else {
                                throw new Exception($"Failed to get resource reference count!");
                            }
                        }

                        var newCount = referenceCount - 1;
                        if (newCount <= 0) {
                            if (newCount < 0) {
                                throw new Exception($"Tried to release resource when reference count was already zero!");
                            }
                            if (_resourceCache.TryGetValue(type, out var resources)) {
                                if (resources.TryRemove(id, out var resource)) {
                                    resource.Dispose();
                                }
                                else {
                                    throw new Exception($"Failed to remove resource!");
                                }
                            }
                            else {
                                throw new Exception($"Failed to get resource!");
                            }
                        }
                    }
                    else {
                        throw new Exception($"Failed to get resource type lock!");
                    }
                }
                else {
                    throw new Exception($"Failed to get resource type references!");
                }
            }
        }

        public void Dispose() {

        }
    }
}