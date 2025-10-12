using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

namespace WorldBuilder.Lib.Settings {
    /// <summary>
    /// Provides automatic cloning and restoration of settings objects
    /// </summary>
    public static class SettingsCloner {
        /// <summary>
        /// Creates a deep clone of a settings object by copying all properties
        /// </summary>
        public static T Clone<T>(T source) where T : class, new() {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var clone = new T();
            CopyProperties(source, clone);
            return clone;
        }

        /// <summary>
        /// Copies all properties from source to target
        /// </summary>
        [UnconditionalSuppressMessage("Trimming", "IL2075")]
        [UnconditionalSuppressMessage("Trimming", "IL2090")]
        public static void CopyProperties<T>(T source, T target) where T : class {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            var type = typeof(T);
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite);

            foreach (var prop in properties) {
                var value = prop.GetValue(source);

                // Handle nested settings objects
                if (value != null && prop.PropertyType.IsClass && prop.PropertyType != typeof(string)) {
                    // Check if it's a settings object (has properties we should copy)
                    var nestedProps = prop.PropertyType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(p => p.CanRead && p.CanWrite)
                        .ToList();

                    if (nestedProps.Any()) {
                        var targetNested = prop.GetValue(target);
                        if (targetNested != null) {
                            CopyPropertiesNonGeneric(value, targetNested);
                        }
                        continue;
                    }
                }

                // Copy value types and strings directly
                prop.SetValue(target, value);
            }
        }

        [UnconditionalSuppressMessage("Trimming", "IL2075")]
        private static void CopyPropertiesNonGeneric(object source, object target) {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            var type = source.GetType();
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite);

            foreach (var prop in properties) {
                var value = prop.GetValue(source);

                // Handle nested settings objects recursively
                if (value != null && prop.PropertyType.IsClass && prop.PropertyType != typeof(string)) {
                    var nestedProps = prop.PropertyType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(p => p.CanRead && p.CanWrite)
                        .ToList();

                    if (nestedProps.Any()) {
                        var targetNested = prop.GetValue(target);
                        if (targetNested != null) {
                            CopyPropertiesNonGeneric(value, targetNested);
                        }
                        continue;
                    }
                }

                prop.SetValue(target, value);
            }
        }

        /// <summary>
        /// Restores settings from a backup copy
        /// </summary>
        public static void Restore<T>(T source, T target) where T : class {
            CopyProperties(source, target);
        }

        /// <summary>
        /// Resets settings to default values by creating a new instance and copying
        /// </summary>
        public static void ResetToDefaults<T>(T target, Func<T> factory) where T : class {
            var defaults = factory();
            CopyProperties(defaults, target);
        }
    }
}