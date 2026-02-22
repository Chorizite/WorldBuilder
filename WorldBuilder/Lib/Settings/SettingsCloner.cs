using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
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
        public static void ResetToDefaults(WorldBuilderSettings target, ProjectSettings? proj) {
            if (target != null) {
                var defaults = new WorldBuilderSettings { Project = proj };
                DeepCopy(defaults, target);
            }
        }

        /// <summary>
        /// Copies all properties from source to target, recursively handling composite objects.
        /// Primitive properties trigger PropertyChanged for immediate UI binding updates.
        /// </summary>
        [SuppressMessage("Trimming", "IL2075:'this' argument does not satisfy 'DynamicallyAccessedMembersAttribute' in call to target method. The return value of the source method does not have matching annotations.", Justification = "<Pending>")]
        private static void DeepCopy(object source, object target) {

            var sourceType = source.GetType();
            var targetType = target.GetType();

            foreach (var sourceProperty in sourceType.GetProperties()) {
                if (!sourceProperty.CanRead) continue;

                var targetProperty = targetType.GetProperty(sourceProperty.Name);
                if (targetProperty == null || !targetProperty.CanWrite) continue;

                var sourceValue = sourceProperty.GetValue(source);
                var targetValue = targetProperty.GetValue(target);

                // Check if this is a composite object (not a primitive, string, or collection)
                if (IsCompositeObject(sourceProperty.PropertyType)) {
                    if (targetValue != null && sourceValue != null) {
                        // Recursively copy nested properties
                        DeepCopy(sourceValue, targetValue);
                    }
                    else if (sourceValue != null && targetValue == null) {
                        // If target is null but source isn't, set it directly
                        targetProperty.SetValue(target, sourceValue);
                        RaisePropertyChanged(target, sourceProperty.Name);
                    }
                }
                else {
                    // For primitive properties, just copy the value
                    targetProperty.SetValue(target, sourceValue);
                    RaisePropertyChanged(target, sourceProperty.Name);
                }
            }
        }

        /// <summary>
        /// Determines if a type is a composite object (not a primitive, string, or collection)
        /// </summary>
        private static bool IsCompositeObject(Type type) {
            // Exclude primitives, strings, and common value types
            if (type.IsPrimitive || type == typeof(string) || type.IsValueType) {
                return false;
            }

            // Exclude collections and enumerables (but not strings, which we already excluded)
            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type)) {
                return false;
            }

            // Exclude common framework types
            if (type.Namespace?.StartsWith("System") == true) {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Manually raises PropertyChanged if the object implements INotifyPropertyChanged
        /// </summary>
        [SuppressMessage("Trimming", "IL2075:'this' argument does not satisfy 'DynamicallyAccessedMembersAttribute' in call to target method. The return value of the source method does not have matching annotations.", Justification = "<Pending>")]
        private static void RaisePropertyChanged(object target, string propertyName) {
            if (target is INotifyPropertyChanged) {
                var type = target.GetType();
                var method = type.GetMethod("OnPropertyChanged",
                    BindingFlags.NonPublic |
                    BindingFlags.Instance,
                    null,
                    [typeof(string)],
                    null);

                method?.Invoke(target, [propertyName]);
            }
        }
    }
}