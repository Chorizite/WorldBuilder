using System;
using System.Text.Json;
using WorldBuilder.Services;

namespace WorldBuilder.Lib.Settings {
    /// <summary>
    /// Provides automatic cloning and restoration of settings objects using JSON serialization
    /// </summary>
    public static class SettingsCloner {
        /// <summary>
        /// Creates a deep clone of a settings object
        /// </summary>
        public static WorldBuilderSettings Clone(WorldBuilderSettings source) {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var json = JsonSerializer.Serialize(source, SourceGenerationContext.Default.WorldBuilderSettings);
            var clone = JsonSerializer.Deserialize(json, SourceGenerationContext.Default.WorldBuilderSettings)
                   ?? throw new InvalidOperationException("Failed to deserialize settings clone");

            if (source.Project != null) {
                var projectJson = JsonSerializer.Serialize(source.Project, SourceGenerationContext.Default.ProjectSettings);
                clone.Project = JsonSerializer.Deserialize(projectJson, SourceGenerationContext.Default.ProjectSettings);
                if (clone.Project != null) {
                    clone.Project.FilePath = source.Project.FilePath;
                }
            }

            return clone;
        }

        /// <summary>
        /// Restores settings from a backup copy
        /// </summary>
        public static void Restore(WorldBuilderSettings source, WorldBuilderSettings target) {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            var clone = Clone(source);
            target.App = clone.App;
            target.Landscape = clone.Landscape;
            if (clone.Project != null && target.Project != null) {
                target.Project = clone.Project;
            }
        }

        /// <summary>
        /// Resets settings to default values
        /// </summary>
        public static void ResetToDefaults(WorldBuilderSettings target,
            Func<WorldBuilderSettings> factory) {
            if (target == null) throw new ArgumentNullException(nameof(target));

            var defaults = factory();
            target.App = defaults.App;
            target.Landscape = defaults.Landscape;

            if (target.Project != null) {
                var filePath = target.Project.FilePath;
                target.Project = new ProjectSettings { FilePath = filePath };
            }
        }
    }
}