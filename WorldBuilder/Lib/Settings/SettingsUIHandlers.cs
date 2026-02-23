using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.Threading.Tasks;

using WorldBuilder.Shared.Lib.Settings;

namespace WorldBuilder.Lib.Settings {
    /// <summary>
    /// Provides dynamic event handlers for settings UI controls
    /// </summary>
    public class SettingsUIHandlers {
        private readonly Window _window;

        public SettingsUIHandlers(Window window) {
            _window = window;
        }

        /// <summary>
        /// Attach browse button handler for path settings
        /// </summary>
        public void AttachBrowseHandler(Button button, SettingPathAttribute pathAttr, TextBox targetTextBox) {
            button.Click += async (s, e) => await BrowseForPath(pathAttr, targetTextBox);
        }

        private async Task BrowseForPath(SettingPathAttribute pathAttr, TextBox targetTextBox) {
            var topLevel = TopLevel.GetTopLevel(_window);
            if (topLevel == null) return;

            switch (pathAttr.Type) {
                case PathType.Folder:
                    var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions {
                        Title = pathAttr.DialogTitle ?? "Select Folder",
                        AllowMultiple = false
                    });

                    if (folders.Count > 0) {
                        targetTextBox.Text = folders[0].Path.LocalPath;
                    }
                    break;

                case PathType.OpenFile:
                    var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
                        Title = pathAttr.DialogTitle ?? "Select File",
                        AllowMultiple = false
                    });

                    if (files.Count > 0) {
                        targetTextBox.Text = files[0].Path.LocalPath;
                    }
                    break;

                case PathType.SaveFile:
                    var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
                        Title = pathAttr.DialogTitle ?? "Save File"
                    });

                    if (file != null) {
                        targetTextBox.Text = file.Path.LocalPath;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Extension methods for attaching settings metadata to controls
    /// </summary>
    public static class SettingsControlExtensions {
        private const string MetadataKey = "SettingMetadata";
        private const string InstanceKey = "SettingInstance";

        public static void SetSettingMetadata(this Control control, SettingPropertyMetadata metadata) {
            control.SetValue(Control.TagProperty, new SettingControlTag {
                Metadata = metadata
            });
        }

        public static SettingPropertyMetadata? GetSettingMetadata(this Control control) {
            return (control.GetValue(Control.TagProperty) as SettingControlTag)?.Metadata;
        }

        private class SettingControlTag {
            public SettingPropertyMetadata? Metadata { get; set; }
        }
    }
}