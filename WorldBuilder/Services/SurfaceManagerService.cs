using Chorizite.OpenGLSDLBackend;
using Chorizite.OpenGLSDLBackend.Lib;
using DatReaderWriter.DBObjs;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Services {
    /// <summary>
    /// Provides shared LandSurfaceManager instances per region to allow resource reuse and prevent VRAM leaks.
    /// </summary>
    public class SurfaceManagerService : IDisposable {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<SurfaceManagerService> _logger;
        
        private class SurfaceManagerEntry {
            public LandSurfaceManager? Manager;
            public int RefCount;
        }

        private readonly Dictionary<(IDatReaderWriter, uint), SurfaceManagerEntry> _managers = new();

        public SurfaceManagerService(ILoggerFactory loggerFactory) {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<SurfaceManagerService>();
        }

        public LandSurfaceManager? GetSurfaceManager(OpenGLGraphicsDevice graphicsDevice, IDatReaderWriter dats, Region region, uint regionId) {
            lock (_managers) {
                var key = (dats, regionId);
                if (!_managers.TryGetValue(key, out var entry)) {
                    _logger.LogInformation("Creating new shared LandSurfaceManager for region {RegionId}", regionId);
                    entry = new SurfaceManagerEntry {
                        Manager = new LandSurfaceManager(graphicsDevice, dats, region, _loggerFactory.CreateLogger<LandSurfaceManager>()),
                        RefCount = 0
                    };
                    _managers[key] = entry;
                }
                entry.RefCount++;
                return entry.Manager;
            }
        }

        public void ReleaseSurfaceManager(IDatReaderWriter dats, uint regionId) {
            lock (_managers) {
                var key = (dats, regionId);
                if (_managers.TryGetValue(key, out var entry)) {
                    entry.RefCount--;
                    if (entry.RefCount <= 0) {
                        _logger.LogInformation("Disposing shared LandSurfaceManager for region {RegionId}", regionId);
                        entry.Manager?.Dispose();
                        _managers.Remove(key);
                    }
                }
            }
        }

        public void Dispose() {
            lock (_managers) {
                foreach (var entry in _managers.Values) {
                    entry.Manager?.Dispose();
                }
                _managers.Clear();
            }
        }
    }
}
