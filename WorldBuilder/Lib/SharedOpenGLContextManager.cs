using Avalonia.OpenGL;
using Chorizite.OpenGLSDLBackend;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using System.Collections.Concurrent;
using WorldBuilder.Services;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Views;

namespace WorldBuilder.Lib {
    /// <summary>
    /// Manages shared OpenGL contexts for multiple windows while maintaining independent viewport states
    /// </summary>
    public class SharedOpenGLContextManager {
        private readonly ILogger _logger;
        private readonly WorldBuilderSettings _settings;
        private readonly CommandLineOptions _commandLineOptions;
        private IGlContext? _masterContext;
        private GL? _masterGL;

        /// <summary>
        /// Gets the OpenGL version string of the master context.
        /// </summary>
        public string? GlVersion { get; private set; }

        /// <summary>
        /// Gets whether bindless texturing is supported and allowed by the master context.
        /// </summary>
        public bool HasBindless { get; private set; }

        // Track viewport dimensions per context to maintain independence between windows
        private readonly ConcurrentDictionary<IGlContext, (int width, int height)> _viewportDimensions = new();

        public SharedOpenGLContextManager(WorldBuilderSettings settings, CommandLineOptions commandLineOptions) {
            _settings = settings;
            _commandLineOptions = commandLineOptions;
            _logger = new ColorConsoleLogger("SharedOpenGLContextManager", () => new ColorConsoleLoggerConfiguration());
        }

        /// <summary>
        /// Sets the master context from which other contexts will share resources
        /// </summary>
        public void SetMasterContext(IGlContext context, GL gl) {
            _masterContext = context;
            _masterGL = gl;
            try {
                var version = gl.GetStringS(Silk.NET.OpenGL.StringName.Version);
                GlVersion = string.IsNullOrWhiteSpace(version) ? "GL: Unknown" : version;

                if (!_commandLineOptions.DisableBindless && _settings.App.AllowBindless && gl.TryGetExtension(out Silk.NET.OpenGL.Extensions.ARB.ArbBindlessTexture ext)) {
                    HasBindless = true;
                } else {
                    HasBindless = false;
                }
            } catch {
                GlVersion = "GL: Unknown";
                HasBindless = false;
            }
            _logger.LogInformation("Master context set for sharing. Version: {Version}, Bindless: {HasBindless} (Settings: {AllowBindless}, CMD: {DisableBindless})", 
                GlVersion, HasBindless, _settings.App.AllowBindless, _commandLineOptions.DisableBindless);
        }

        /// <summary>
        /// Gets the master context for sharing
        /// </summary>
        public (IGlContext? context, GL? gl) GetMasterContext() {
            return (_masterContext, _masterGL);
        }

        /// <summary>
        /// Creates a shared context from the master context
        /// Note: Avalonia's IGlContext doesn't directly support sharing,
        /// so we'll pass the same GL instance and manage resource sharing at the GameScene level
        /// </summary>
        public (IGlContext? context, GL? gl) GetSharedContext() {
            // For now, return the master context info since Avalonia handles context internally
            return (_masterContext, _masterGL);
        }

        /// <summary>
        /// Stores viewport dimensions for a specific context
        /// </summary>
        public void SetViewportDimensions(IGlContext context, int width, int height) {
            _viewportDimensions.AddOrUpdate(context, (width, height), (ctx, dims) => (width, height));
            _logger.LogDebug("Set viewport dimensions for context {Context}: {Width}x{Height}", context.GetHashCode(), width, height);
        }

        /// <summary>
        /// Gets viewport dimensions for a specific context
        /// </summary>
        public (int width, int height)? GetViewportDimensions(IGlContext context) {
            if (_viewportDimensions.TryGetValue(context, out var dims)) {
                _logger.LogDebug("Retrieved viewport dimensions for context {Context}: {Width}x{Height}",
                                context.GetHashCode(), dims.width, dims.height);
                return dims;
            }
            else {
                _logger.LogDebug("No viewport dimensions found for context {Context}", context.GetHashCode());
                return null;
            }
        }

        /// <summary>
        /// Removes viewport dimensions when context is destroyed
        /// </summary>
        public void RemoveViewportDimensions(IGlContext context) {
            var removed = _viewportDimensions.TryRemove(context, out _);
            if (removed) {
                _logger.LogDebug("Removed viewport dimensions for context {Context}", context.GetHashCode());
            }
        }
    }
}