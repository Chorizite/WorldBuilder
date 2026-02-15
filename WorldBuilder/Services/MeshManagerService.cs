using Chorizite.OpenGLSDLBackend;
using Chorizite.OpenGLSDLBackend.Lib;
using DatReaderWriter;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using System;
using System.Collections.Concurrent;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Services {
    /// <summary>
    /// Provides shared ObjectMeshManager instances per dat reader to allow resource reuse.
    /// </summary>
    public class MeshManagerService : IDisposable {
        private readonly ILogger<MeshManagerService> _logger;
        private readonly ConcurrentDictionary<IDatReaderWriter, ObjectMeshManager> _managers = new();

        public MeshManagerService(ILogger<MeshManagerService> logger) {
            _logger = logger;
        }

        public ObjectMeshManager GetMeshManager(OpenGLGraphicsDevice graphicsDevice, IDatReaderWriter dats) {
            return _managers.GetOrAdd(dats, _ => {
                _logger.LogInformation("Creating new shared ObjectMeshManager for dats");
                return new ObjectMeshManager(graphicsDevice, dats);
            });
        }

        public void Dispose() {
            foreach (var manager in _managers.Values) {
                manager.Dispose();
            }
            _managers.Clear();
        }
    }
}
