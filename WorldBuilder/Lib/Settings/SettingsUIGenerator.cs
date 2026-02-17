using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Reflection;
using WorldBuilder.Lib.Converters;

namespace WorldBuilder.Lib.Settings {
    /// <summary>
    /// Generates Avalonia UI controls dynamically from settings metadata
    /// </summary>
    public class SettingsUIGenerator {
        private readonly object _settingsRoot;
        private readonly SettingsMetadataProvider _metadata;
        private readonly SettingsUIHandlers? _handlers;
        private readonly Dictionary<Type, Func<SettingPropertyMetadata, object, string, Control?>> _controlFactories;

        public SettingsUIGenerator(object settingsRoot, Window? window = null) {
            _settingsRoot = settingsRoot;
            _metadata = new SettingsMetadataProvider(settingsRoot.GetType());
            _handlers = window != null ? new SettingsUIHandlers(window) : null;

            _controlFactories = new Dictionary<Type, Func<SettingPropertyMetadata, object, string, Control?>> {
                { typeof(bool), CreateBoolControl },
                { typeof(int), CreateNumericControl },
                { typeof(long), CreateNumericControl },
                { typeof(float), CreateNumericControl },
                { typeof(double), CreateNumericControl },
                { typeof(decimal), CreateNumericControl },
                { typeof(short), CreateNumericControl },
                { typeof(byte), CreateNumericControl },
                { typeof(string), CreateStringControl },
                { typeof(LogLevel), CreateEnumControl },
                { typeof(Vector3), CreateVector3Control }
            };
        }

        /// <summary>
        /// Generate navigation items for the settings categories
        /// </summary>
        public ListBox GenerateNavigation() {
            var listBox = new ListBox { Margin = new Thickness(0, 16, 0, 0) };

            foreach (var category in _metadata.RootCategories.OrderBy(c => c.Order)) {
                AddNavigationItems(listBox, category, isRoot: true);
            }

            return listBox;
        }

        private void AddNavigationItems(ListBox listBox, SettingCategoryMetadata category, bool isRoot,
            string? parentTag = null) {
            var tag = string.IsNullOrEmpty(parentTag)
                ? category.Name.ToLower().Replace(" ", "-")
                : $"{parentTag}-{category.Name.ToLower().Replace(" ", "-")}";

            var item = new ListBoxItem {
                Content = category.Name,
                Tag = tag,
                Classes = { isRoot ? "NavSection" : "NavSubSection" },
                IsEnabled = category.Properties.Any()
            };

            listBox.Items.Add(item);

            // Add subcategories
            foreach (var subCategory in category.SubCategories.OrderBy(c => c.Order)) {
                AddNavigationItems(listBox, subCategory, isRoot: false, tag);
            }
        }

        /// <summary>
        /// Generate content panels for all categories
        /// </summary>
        public Panel GenerateContentPanels() {
            var panel = new Panel();

            foreach (var category in _metadata.RootCategories) {
                GenerateCategoryPanels(panel, category);
            }

            return panel;
        }

        private void GenerateCategoryPanels(Panel parent, SettingCategoryMetadata category, string? parentTag = null) {
            var tag = string.IsNullOrEmpty(parentTag)
                ? category.Name.ToLower().Replace(" ", "-")
                : $"{parentTag}-{category.Name.ToLower().Replace(" ", "-")}";

            var scrollViewer = new ScrollViewer { Name = $"{tag.Replace("-", "")}Panel", IsVisible = false };

            var stackPanel = new StackPanel { Margin = new Thickness(16) };

            // Add title
            stackPanel.Children.Add(new TextBlock {
                Text = category.Name,
                FontSize = 16,
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 0, 0, 12)
            });

            // Add property controls
            var categoryInstance = GetCategoryInstance(category.Type);
            if (categoryInstance != null) {
                foreach (var property in category.Properties) {
                    var control = GeneratePropertyControl(property, categoryInstance);
                    if (control != null) {
                        stackPanel.Children.Add(control);
                    }
                }
            }

            scrollViewer.Content = stackPanel;
            parent.Children.Add(scrollViewer);

            // Generate panels for subcategories
            foreach (var subCategory in category.SubCategories) {
                GenerateCategoryPanels(parent, subCategory, tag);
            }
        }

        private object? GetCategoryInstance(Type categoryType) {
            return FindInstance(categoryType, _settingsRoot);
        }

        private object? FindInstance(Type targetType, object current) {
            if (current.GetType() == targetType) return current;

            var properties = current.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType.IsClass && p.PropertyType != typeof(string) && p.GetMethod != null);

            foreach (var prop in properties) {
                var child = prop.GetValue(current);
                if (child != null) {
                    var found = FindInstance(targetType, child);
                    if (found != null) return found;
                }
            }

            return null;
        }

        [UnconditionalSuppressMessage("Trimming",
            "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
            Justification = "<Pending>")]
        private Control? GeneratePropertyControl(SettingPropertyMetadata metadata, object instance) {
            var border = new Border { Classes = { "SettingGroup" }, Margin = new Thickness(0, 0, 0, 16) };

            var stackPanel = new StackPanel();
            var bindingPath = metadata.Property.Name;

            // Label with value display if applicable
            if (metadata.Range != null || !string.IsNullOrEmpty(metadata.Format)) {
                var dockPanel = new DockPanel();

                if (!string.IsNullOrEmpty(metadata.Format)) {
                    var valueDisplay = new TextBlock {
                        Margin = new Thickness(8, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    valueDisplay.Bind(TextBlock.TextProperty,
                        new Binding { Source = instance, Path = bindingPath, StringFormat = metadata.Format });
                    DockPanel.SetDock(valueDisplay, Dock.Right);
                    dockPanel.Children.Add(valueDisplay);
                }

                dockPanel.Children.Add(new TextBlock { Classes = { "SettingLabel" }, Text = metadata.DisplayName });

                stackPanel.Children.Add(dockPanel);
            }
            else {
                stackPanel.Children.Add(new TextBlock { Classes = { "SettingLabel" }, Text = metadata.DisplayName });
            }

            // Description
            if (!string.IsNullOrEmpty(metadata.Description)) {
                stackPanel.Children.Add(new TextBlock {
                    Classes = { "SettingDescription" },
                    Text = metadata.Description
                });
            }

            // Input control
            var inputControl = CreateInputControl(metadata, instance, bindingPath);
            if (inputControl != null) {
                stackPanel.Children.Add(inputControl);
            }

            border.Child = stackPanel;
            return border;
        }

        [UnconditionalSuppressMessage("Trimming",
            "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
            Justification = "<Pending>")]
        private Control? CreateInputControl(SettingPropertyMetadata metadata, object instance, string bindingPath) {
            var propType = metadata.Property.PropertyType;

            if (_controlFactories.TryGetValue(propType, out var factory)) {
                return factory(metadata, instance, bindingPath);
            }

            return CreateDefaultControl(metadata, instance, bindingPath);
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "<Pending>")]
        private Control? CreateBoolControl(SettingPropertyMetadata metadata, object instance, string bindingPath) {
            var checkBox = new CheckBox { Content = $"Enable {metadata.DisplayName.ToLower()}" };
            checkBox.Bind(ToggleButton.IsCheckedProperty,
                new Binding { Source = instance, Path = bindingPath, Mode = BindingMode.TwoWay });
            return checkBox;
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "<Pending>")]
        private Control? CreateNumericControl(SettingPropertyMetadata metadata, object instance, string bindingPath) {
            if (metadata.Range != null) {
                var slider = new Slider {
                    Minimum = metadata.Range.Minimum,
                    Maximum = metadata.Range.Maximum,
                    SmallChange = metadata.Range.SmallChange,
                    LargeChange = metadata.Range.LargeChange
                };
                slider.Bind(Slider.ValueProperty,
                    new Binding { Source = instance, Path = bindingPath, Mode = BindingMode.TwoWay });
                return slider;
            }

            return CreateDefaultControl(metadata, instance, bindingPath);
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "<Pending>")]
        private Control? CreateStringControl(SettingPropertyMetadata metadata, object instance, string bindingPath) {
            if (metadata.Path != null) {
                var dockPanel = new DockPanel();

                var button = new Button { Width = 80, Content = "Browse...", Margin = new Thickness(8, 0, 0, 0) };
                DockPanel.SetDock(button, Dock.Right);
                dockPanel.Children.Add(button);

                var textBox = new TextBox { Watermark = metadata.Path.DialogTitle ?? "Select path..." };
                textBox.Bind(TextBox.TextProperty,
                    new Binding { Source = instance, Path = bindingPath, Mode = BindingMode.TwoWay });
                dockPanel.Children.Add(textBox);

                if (_handlers != null && metadata.Path != null) {
                    _handlers.AttachBrowseHandler(button, metadata.Path, textBox);
                }

                return dockPanel;
            }

            return CreateDefaultControl(metadata, instance, bindingPath);
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "<Pending>")]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "<Pending>")]
        private Control? CreateEnumControl(SettingPropertyMetadata metadata, object instance, string bindingPath) {
            var comboBox = new ComboBox {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = Enum.GetValues(metadata.Property.PropertyType)
            };
            comboBox.Bind(SelectingItemsControl.SelectedItemProperty,
                new Binding { Source = instance, Path = bindingPath, Mode = BindingMode.TwoWay });
            return comboBox;
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "<Pending>")]
        private Control? CreateVector3Control(SettingPropertyMetadata metadata, object instance, string bindingPath) {
            var colorPicker = new Avalonia.Controls.ColorPicker { HorizontalAlignment = HorizontalAlignment.Left };

            var converter = new Vector3ToColorConverter();
            colorPicker.Bind(Avalonia.Controls.ColorPicker.ColorProperty,
                new Binding {
                    Source = instance,
                    Path = bindingPath,
                    Mode = BindingMode.TwoWay,
                    Converter = converter
                });
            return colorPicker;
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "<Pending>")]
        private Control? CreateDefaultControl(SettingPropertyMetadata metadata, object instance, string bindingPath) {
            var defaultTextBox = new TextBox();
            defaultTextBox.Bind(TextBox.TextProperty,
                new Binding { Source = instance, Path = bindingPath, Mode = BindingMode.TwoWay });
            return defaultTextBox;
        }
    }
}