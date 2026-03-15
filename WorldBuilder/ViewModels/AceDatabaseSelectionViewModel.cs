using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WorldBuilder.Lib;
using WorldBuilder.Services;
using WorldBuilder.Shared.Lib.Settings;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.ViewModels {
    /// <summary>
    /// ViewModel for selecting an ACE database, used in both project creation and project settings.
    /// </summary>
    public partial class AceDatabaseSelectionViewModel : ObservableObject {
        private readonly ILogger<AceDatabaseSelectionViewModel> _log;
        private readonly IAceRepositoryService _aceRepository;
        private readonly WorldBuilderSettings _settings;
        private readonly ProjectManager _projectManager;

        [ObservableProperty]
        private AceSourceType _selectedAceSourceType = AceSourceType.None;

        [ObservableProperty]
        private string _localAceDbPath = string.Empty;

        [ObservableProperty]
        private ManagedAceDb? _selectedManagedAceDb;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _loadingStatus = string.Empty;

        [ObservableProperty]
        private float _loadingProgress;

        public ObservableCollection<ManagedAceDb> ManagedAceDbs { get; } = [];

        public AceSourceType[] AceSourceTypes { get; } = Enum.GetValues<AceSourceType>();

        public event EventHandler<Guid?>? DatabaseSelected;

        public AceDatabaseSelectionViewModel() {
            // Design-time constructor
            _log = Microsoft.Extensions.Logging.Abstractions.NullLogger<AceDatabaseSelectionViewModel>.Instance;
            _aceRepository = null!;
            _settings = null!;
            _projectManager = null!;
        }

        public AceDatabaseSelectionViewModel(ILogger<AceDatabaseSelectionViewModel> log, IAceRepositoryService aceRepository, WorldBuilderSettings settings, ProjectManager projectManager) {
            _log = log;
            _aceRepository = aceRepository;
            _settings = settings;
            _projectManager = projectManager;

            UpdateManagedResources();

            // Set initial selection from current project if any
            if (_projectManager.CurrentProject?.ManagedAceDbId != null) {
                SelectedAceSourceType = AceSourceType.Managed;
                SelectedManagedAceDb = ManagedAceDbs.FirstOrDefault(d => d.Id == _projectManager.CurrentProject.ManagedAceDbId);
            }

            PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(SelectedAceSourceType) || 
                    e.PropertyName == nameof(SelectedManagedAceDb) || 
                    e.PropertyName == nameof(LocalAceDbPath)) {
                    UpdateSelection();
                }
            };
        }

        private void UpdateSelection() {
            if (SelectedAceSourceType == AceSourceType.None) {
                DatabaseSelected?.Invoke(this, null);
            }
            else if (SelectedAceSourceType == AceSourceType.Managed) {
                DatabaseSelected?.Invoke(this, SelectedManagedAceDb?.Id);
            }
        }

        public void UpdateManagedResources() {
            if (_aceRepository == null) return;

            var currentRoot = _aceRepository.RepositoryRoot;
            if (string.IsNullOrEmpty(currentRoot) && _projectManager.CurrentProject != null) {
                var serverSiblingDir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(_projectManager.CurrentProject.ProjectDirectory) ?? string.Empty) ?? string.Empty, "Server");
                _aceRepository.SetRepositoryRoot(serverSiblingDir);
            }

            ManagedAceDbs.Clear();
            foreach (var db in _aceRepository.GetManagedAceDbs()) {
                ManagedAceDbs.Add(db);
            }
        }

        [RelayCommand]
        private async Task DownloadLatestAceDb() {
            IsLoading = true;
            LoadingStatus = "Preparing download...";
            LoadingProgress = 0f;

            var progress = new Progress<(string message, float progress)>(p => {
                LoadingStatus = p.message;
                LoadingProgress = p.progress * 100f;
            });

            var result = await _aceRepository.DownloadLatestAsync(progress, default);
            
            IsLoading = false;

            if (result.IsSuccess) {
                UpdateManagedResources();
                SelectedAceSourceType = AceSourceType.Managed;
                SelectedManagedAceDb = ManagedAceDbs.FirstOrDefault(d => d.Id == result.Value.Id);
            }
            else {
                _log.LogError("Failed to download ACE DB: {Error}", result.Error.Message);
            }
        }

        [RelayCommand]
        private async Task BrowseLocalAceDb(Avalonia.Controls.Window parentWindow) {
            var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(parentWindow);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions() {
                Title = "Choose ACE SQLite database",
                AllowMultiple = false,
                SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(_settings.App.ProjectsDirectory),
                FileTypeFilter = new[] {
                    new FilePickerFileType("SQLite Database") {
                        Patterns = new[] { "*.db", "*.sqlite" }
                    }
                }
            });

            if (files.Count == 0) return;

            var localPath = files[0].TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(localPath)) {
                LocalAceDbPath = localPath;
                
                // Import local DB immediately
                await ImportLocalDb();
            }
        }

        private async Task ImportLocalDb() {
            if (string.IsNullOrEmpty(LocalAceDbPath)) return;

            IsLoading = true;
            LoadingStatus = "Importing local ACE database...";
            
            var progress = new Progress<(string message, float progress)>(p => {
                LoadingStatus = p.message;
                LoadingProgress = p.progress * 100f;
            });

            var result = await _aceRepository.ImportAsync(LocalAceDbPath, null, progress, default);
            
            IsLoading = false;

            if (result.IsSuccess) {
                UpdateManagedResources();
                SelectedAceSourceType = AceSourceType.Managed;
                SelectedManagedAceDb = ManagedAceDbs.FirstOrDefault(d => d.Id == result.Value.Id);
            }
            else {
                _log.LogError("Failed to import ACE DB: {Error}", result.Error.Message);
            }
        }
    }
}
