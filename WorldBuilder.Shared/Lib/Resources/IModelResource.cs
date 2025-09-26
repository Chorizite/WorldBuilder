using System;
using System.Resources;
using System.Threading.Tasks;

namespace WorldBuilder.Shared.Lib.Resources {
    public abstract class IModelResource : IResource {
        private bool _disposed;
        private object lockObj = new object();

        protected bool _startedLoading;
        protected Task<bool>? _loadTask;
        public virtual bool IsLoading { get; protected set; } = true;

        public IModelResource(uint id, ResourceManager resourceManager) : base(id, resourceManager) {

        }

        public IModelResource(ResourceManager resourceManager) : this(0, resourceManager) {

        }

        public virtual async Task<bool> Load() {
            var needsLoad = false;
            lock (lockObj) {
                if (_startedLoading) {
                    if (_loadTask is not null) {
                        needsLoad = true;
                    }
                    else {
                        throw new Exception($"_loadTask was null after _startedLoading!");
                    }
                }
            }

            if (needsLoad) {
                if (_loadTask is not null) {
                    return await _loadTask;
                }
                else {
                    throw new Exception($"_loadTask was null after _startedLoading!");
                }
            }

            lock (lockObj) {
                _startedLoading = true;
                _loadTask = LoadInternal();
            }

            bool res;
            try {
                res = await _loadTask;
            }
            catch (TaskCanceledException) {
                return false;
            }
            if (_disposed) {
                return false;
            }

            IsLoading = false;
            return res;
        }

        protected abstract Task<bool> LoadInternal();

        public override void Dispose() {
            _disposed = true;
            base.Dispose();
        }
    }
}