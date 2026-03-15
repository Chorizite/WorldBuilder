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

        private Guid? _managedDatId;

        /// <summary>
        /// Gets or sets the managed DAT set ID, if any.
        /// </summary>
        public Guid? ManagedDatId {
            get => _managedDatId;
            set => SetProperty(ref _managedDatId, value);
        }

        private Guid? _managedAceId;

        /// <summary>
        /// Gets or sets the managed ACE DB ID, if any.
        /// </summary>
        public Guid? ManagedAceId {
            get => _managedAceId;
            set => SetProperty(ref _managedAceId, value);
        }

        private string? _versionInfo;

        /// <summary>
        /// Gets or sets the version information for a managed DAT set.
        /// </summary>
        public string? VersionInfo {
            get => _versionInfo;
            set => SetProperty(ref _versionInfo, value);
        }

        /// <summary>
        /// Gets the display subtext for the project (either version info or file path).
        /// </summary>
        [JsonIgnore]
        public string DisplaySubText => VersionInfo ?? FilePath;

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

        private string? _error;

        /// <summary>
        /// Gets or sets the error message if the project has one.
        /// </summary>
        [JsonIgnore]
        public string? Error {
            get => _error;
            set {
                if (SetProperty(ref _error, value)) {
                    OnPropertyChanged(nameof(HasError));
                    OnPropertyChanged(nameof(TooltipText));
                }
            }
        }

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
        /// Verifies that the project file and its managed DAT set (if any) exist.
        /// </summary>
        /// <param name="datRepository">The DAT repository service to check for managed sets</param>
        /// <param name="aceRepository">The ACE repository service to check for managed ACE DBs</param>
        /// <returns>A task that resolves to true if the project and its dependencies exist, false otherwise</returns>
        internal Task<bool> Verify(WorldBuilder.Shared.Services.IDatRepositoryService datRepository, WorldBuilder.Shared.Services.IAceRepositoryService aceRepository) {
            if (!File.Exists(FilePath)) {
                Error = "File no longer exists";
                return Task.FromResult(false);
            }

            if (ManagedDatId.HasValue && datRepository.GetManagedDataSet(ManagedDatId.Value) == null) {
                Error = "Managed DAT set no longer exists";
                return Task.FromResult(false);
            }

            Error = null;
            return Task.FromResult(true);
        }
    }
}