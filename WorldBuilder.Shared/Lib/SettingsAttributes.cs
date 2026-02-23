using System;

namespace WorldBuilder.Shared.Lib.Settings {
    /// <summary>
    /// Defines a setting category for organizing settings in the UI
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class SettingCategoryAttribute : Attribute {
        public string Name { get; set; }
        public string? ParentCategory { get; set; }
        public int Order { get; set; }

        public SettingCategoryAttribute(string name, string? parentCategory = null) {
            Name = name;
            ParentCategory = parentCategory;
        }
    }

    /// <summary>
    /// Provides description text for a setting
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class SettingDescriptionAttribute : Attribute {
        public string Description { get; set; }

        public SettingDescriptionAttribute(string description) {
            Description = description;
        }
    }

    /// <summary>
    /// Defines range constraints for numeric settings
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class SettingRangeAttribute : Attribute {
        public double Minimum { get; set; }
        public double Maximum { get; set; }
        public double SmallChange { get; set; }
        public double LargeChange { get; set; }

        public SettingRangeAttribute(double min, double max, double smallChange, double largeChange) {
            Minimum = min;
            Maximum = max;
            SmallChange = smallChange;
            LargeChange = largeChange;
        }
    }

    /// <summary>
    /// Specifies display name for a setting (if different from property name)
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class SettingDisplayNameAttribute : Attribute {
        public string DisplayName { get; set; }

        public SettingDisplayNameAttribute(string displayName) {
            DisplayName = displayName;
        }
    }

    /// <summary>
    /// Marks a property as hidden from the settings UI
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class SettingHiddenAttribute : Attribute {
    }

    /// <summary>
    /// Defines the order of settings within a category
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class SettingOrderAttribute : Attribute {
        public int Order { get; set; }

        public SettingOrderAttribute(int order) {
            Order = order;
        }
    }

    /// <summary>
    /// Specifies which control type to use for file/folder paths
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class SettingPathAttribute : Attribute {
        public PathType Type { get; set; }
        public string? DialogTitle { get; set; }

        public SettingPathAttribute(PathType type) {
            Type = type;
        }
    }

    public enum PathType {
        Folder,
        OpenFile,
        SaveFile
    }

    /// <summary>
    /// Provides format string for displaying values
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class SettingFormatAttribute : Attribute {
        public string Format { get; set; }

        public SettingFormatAttribute(string format) {
            Format = format;
        }
    }
}