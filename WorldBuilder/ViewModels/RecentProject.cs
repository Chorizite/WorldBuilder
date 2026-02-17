using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.IO;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.ViewModels {
    /// <summary>
    /// Represents a recent project entry in the application.
    /// </summary>
    public partial class RecentProject : ObservableObject {
        private string _name = string.Empty;

        /// <summary>
        /// Gets or sets the name of the project.
        /// </summary>
        public string Name {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private string _filePath = string.Empty;

        /// <summary>
        /// Gets or sets the file path of the project.
        /// </summary>
        public string FilePath {
            get => _filePath;
            set => SetProperty(ref _filePath, value);
        }

        private DateTime _lastOpened;

        /// <summary>
        /// Gets or sets the date and time when the project was last opened.
        /// </summary>
        public DateTime LastOpened {
            get => _lastOpened;
            set => SetProperty(ref _lastOpened, value);
        }

        private bool _isReadOnly;

        /// <summary>
        /// Gets or sets a value indicating whether the project is read-only.
        /// </summary>
        public bool IsReadOnly {
            get => _isReadOnly;
            set => SetProperty(ref _isReadOnly, value);
        }

        // Your [JsonIgnore] properties remain unchanged
        /// <summary>
        /// Gets a display-formatted string of the last opened date.
        /// </summary>
        [JsonIgnore]
        public string LastOpenedDisplay => LastOpened.ToString("MMM dd, yyyy 'at' h:mm tt");

        /// <summary>
        /// Gets the directory containing the project file.
        /// </summary>
        [JsonIgnore]
        public string FileDirectory => Path.GetDirectoryName(FilePath) ?? string.Empty;

        /// <summary>
        /// Gets the icon glyph for the project.
        /// </summary>
        [JsonIgnore]
        public string IconGlyph => IsReadOnly ? "FolderOpen" : "Library";

        /// <summary>
        /// Gets or sets the error message if the project has one.
        /// </summary>
        [JsonIgnore]
        public string? Error { get; set; }

        /// <summary>
        /// Gets a value indicating whether the project has an error.
        /// </summary>
        [JsonIgnore]
        public bool HasError => !string.IsNullOrEmpty(Error);

        /// <summary>
        /// Gets the tooltip text for the project, showing either the error or file path.
        /// </summary>
        [JsonIgnore]
        public string TooltipText => HasError ? (Error ?? "Unknown error") : FilePath;

        /// <summary>
        /// Verifies that the project file exists and can be opened.
        /// </summary>
        /// <returns>A task that resolves to true if the project is valid, false otherwise</returns>
        internal async Task<bool> Verify() {
            if (!File.Exists(FilePath)) {
                Error = "File no longer exists";
                return false;
            }

            try {
                await Project.Open(FilePath, default);
            }
            catch (Exception ex) {
                Error = ex.Message;
                return false;
            }

            return true;
        }
    }
}