using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

namespace WorldBuilder.Lib.Settings {
    /// <summary>
    /// Metadata about a setting property
    /// </summary>
    public class SettingPropertyMetadata {
        public PropertyInfo Property { get; }
        public string DisplayName { get; }
        public string? Description { get; }
        public SettingRangeAttribute? Range { get; }
        public SettingPathAttribute? Path { get; }
        public string? Format { get; }
        public int Order { get; }
        public bool IsHidden { get; }

        public SettingPropertyMetadata(PropertyInfo property) {
            Property = property;

            // Find backing field for attributes (assuming CommunityToolkit.Mvvm naming convention)
            var fieldName = "_" + char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
            var field = property.DeclaringType?.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);

            var displayNameAttr = field?.GetCustomAttribute<SettingDisplayNameAttribute>();
            DisplayName = displayNameAttr?.DisplayName ?? SplitCamelCase(property.Name);

            Description = field?.GetCustomAttribute<SettingDescriptionAttribute>()?.Description;
            Range = field?.GetCustomAttribute<SettingRangeAttribute>();
            Path = field?.GetCustomAttribute<SettingPathAttribute>();
            Format = field?.GetCustomAttribute<SettingFormatAttribute>()?.Format;
            Order = field?.GetCustomAttribute<SettingOrderAttribute>()?.Order ?? 0;
            IsHidden = field?.GetCustomAttribute<SettingHiddenAttribute>() != null;
        }

        private static string SplitCamelCase(string str) {
            return System.Text.RegularExpressions.Regex.Replace(str, "([a-z])([A-Z])", "$1 $2");
        }
    }

    /// <summary>
    /// Metadata about a settings category
    /// </summary>
    public class SettingCategoryMetadata {
        public Type Type { get; }
        public string Name { get; }
        public string? ParentCategory { get; }
        public int Order { get; }
        public List<SettingPropertyMetadata> Properties { get; }
        public List<SettingCategoryMetadata> SubCategories { get; }

        [UnconditionalSuppressMessage("Trimming", "IL2070")]
        public SettingCategoryMetadata(Type type) {
            Type = type;

            var categoryAttr = type.GetCustomAttribute<SettingCategoryAttribute>();
            Name = categoryAttr?.Name ?? type.Name.Replace("Settings", "");
            ParentCategory = categoryAttr?.ParentCategory;
            Order = categoryAttr?.Order ?? 0;

            Properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite)
                .Select(p => new SettingPropertyMetadata(p))
                .Where(m => !m.IsHidden)
                .OrderBy(m => m.Order)
                .ThenBy(m => m.DisplayName)
                .ToList();

            SubCategories = new List<SettingCategoryMetadata>();
        }
    }

    /// <summary>
    /// Discovers and organizes all settings metadata
    /// </summary>
    public class SettingsMetadataProvider {
        private readonly List<SettingCategoryMetadata> _rootCategories;

        public IReadOnlyList<SettingCategoryMetadata> RootCategories => _rootCategories;

        public SettingsMetadataProvider(Type settingsRootType) {
            var categories = DiscoverCategories(settingsRootType);
            _rootCategories = BuildHierarchy(categories);
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "<Pending>")]
        private List<SettingCategoryMetadata> DiscoverCategories(Type rootType) {
            var assembly = rootType.Assembly;
            return assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.Namespace == "WorldBuilder.Lib.Settings" && t.GetCustomAttribute<SettingCategoryAttribute>() != null)
                .Select(t => new SettingCategoryMetadata(t))
                .ToList();
        }

        private List<SettingCategoryMetadata> BuildHierarchy(List<SettingCategoryMetadata> categories) {
            var roots = new List<SettingCategoryMetadata>();
            var categoryMap = categories.ToDictionary(c => c.Name);

            foreach (var category in categories.OrderBy(c => c.Order)) {
                if (string.IsNullOrEmpty(category.ParentCategory)) {
                    roots.Add(category);
                }
                else if (categoryMap.TryGetValue(category.ParentCategory, out var parent)) {
                    parent.SubCategories.Add(category);
                }
                else {
                    // Parent not found, treat as root
                    roots.Add(category);
                }
            }

            return roots;
        }
    }
}