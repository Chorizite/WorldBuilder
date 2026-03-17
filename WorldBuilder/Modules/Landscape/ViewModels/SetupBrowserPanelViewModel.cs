using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DatReaderWriter.Enums;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Modules.DatBrowser.ViewModels;
using WorldBuilder.Services;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Services;
using System.ComponentModel;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Landscape.ViewModels {
    public partial class SetupBrowserPanelViewModel : ViewModelBase, IDisposable, IKeywordSearchViewModel {
        private readonly IKeywordRepositoryService _keywordRepository;
        private readonly ProjectManager _projectManager;
        private readonly IDatReaderWriter _dats;
        private readonly WorldBuilderSettings _settings;
        private readonly ThemeService _themeService;
        private readonly ObjectManipulationTool _objTool;
        private CancellationTokenSource? _searchCts;
        private bool _isDisposed;

        [ObservableProperty]
        private string _keywordsSearchText = string.Empty;

        [ObservableProperty]
        private bool _isKeywordsSearchEnabled;

        [ObservableProperty]
        private bool _isEmbeddingSearchActive;

        [ObservableProperty]
        private string _keywordsSearchTooltip = string.Empty;

        [ObservableProperty]
        private SearchType _searchType = SearchType.Hybrid;

        public IEnumerable<SearchType> SearchTypes => Enum.GetValues<SearchType>();

        public bool IsObjectToolActive => _objTool.IsActive;

        [ObservableProperty]
        private GridBrowserViewModel? _gridBrowser;

        public SetupBrowserPanelViewModel(
            IKeywordRepositoryService keywordRepository,
            ProjectManager projectManager,
            IDatReaderWriter dats,
            WorldBuilderSettings settings,
            ThemeService themeService,
            ObjectManipulationTool objTool) {
            _keywordRepository = keywordRepository;
            _projectManager = projectManager;
            _dats = dats;
            _settings = settings;
            _themeService = themeService;
            _objTool = objTool;
            _objTool.PropertyChanged += OnToolPropertyChanged;

            _gridBrowser = new GridBrowserViewModel(
                DBObjType.Setup,
                _dats,
                _settings,
                _themeService,
                onSelected: OnSetupSelected) {
                ShowToolbar = false
            };

            _keywordRepository.GlobalProgress += OnKeywordGenerationProgress;
            _projectManager.CurrentProjectChanged += OnProjectChanged;

            UpdateSearchState();
            _ = PerformSearchAsync(string.Empty, SearchType, CancellationToken.None);
        }

        private void OnToolPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(ObjectManipulationTool.IsActive)) {
                OnPropertyChanged(nameof(IsObjectToolActive));
            }
        }

        private void OnSetupSelected(uint setupId) {
            _objTool.EnterPlacementMode(setupId);
        }

        partial void OnKeywordsSearchTextChanged(string value) {
            TriggerSearch();
        }

        partial void OnSearchTypeChanged(SearchType value) {
            TriggerSearch();
        }

        private void TriggerSearch() {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            
            if (GridBrowser != null) {
                GridBrowser.IsKeywordsSearching = !string.IsNullOrWhiteSpace(KeywordsSearchText);
            }
            
            _ = PerformSearchAsync(KeywordsSearchText, SearchType, _searchCts.Token);
        }

        private async Task PerformSearchAsync(string query, SearchType searchType, CancellationToken ct) {
            if (GridBrowser == null) return;

            try {
                if (!string.IsNullOrWhiteSpace(query)) {
                    await Task.Delay(300, ct);
                }

                if (string.IsNullOrWhiteSpace(query)) {
                    GridBrowser.SetFileIds(_dats.Portal.GetAllIdsOfType<DatReaderWriter.DBObjs.Setup>().OrderBy(x => x).ToList());
                    GridBrowser.IsKeywordsSearching = false;
                    return;
                }

                var project = _projectManager.CurrentProject;
                if (project == null) {
                    GridBrowser.IsKeywordsSearching = false;
                    return;
                }

                var datId = project.ManagedIds.ManagedDatSetId ?? Guid.Empty;
                var aceId = project.ManagedIds.ManagedAceDbId ?? Guid.Empty;

                if (!_keywordRepository.AreKeywordsValid(datId, aceId)) {
                    GridBrowser.SetFileIds(Enumerable.Empty<uint>());
                    GridBrowser.IsKeywordsSearching = false;
                    return;
                }

                GridBrowser.IsKeywordsSearching = true;
                try {
                    var results = await _keywordRepository.SearchSetupsAsync(datId, aceId, query, searchType, ct);
                    if (!ct.IsCancellationRequested) {
                        GridBrowser.SetFileIds(results ?? Enumerable.Empty<uint>());
                    }
                }
                finally {
                    GridBrowser.IsKeywordsSearching = false;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) {
                GridBrowser.IsKeywordsSearching = false;
            }
        }

        private void OnKeywordGenerationProgress(object? sender, IKeywordRepositoryService.KeywordGenerationProgress e) {
            UpdateSearchState();
        }

        private void OnProjectChanged(object? sender, EventArgs e) {
            UpdateSearchState();
            KeywordsSearchText = string.Empty;
        }

        private void UpdateSearchState() {
            var project = _projectManager.CurrentProject;
            if (project == null) {
                IsKeywordsSearchEnabled = false;
                KeywordsSearchTooltip = "No project loaded";
                return;
            }

            var datId = project.ManagedIds.ManagedDatSetId ?? Guid.Empty;
            var aceId = project.ManagedIds.ManagedAceDbId ?? Guid.Empty;

            IsKeywordsSearchEnabled = _keywordRepository.CanSearchKeywords(datId, aceId);
            IsEmbeddingSearchActive = _keywordRepository.IsEmbeddingSearchActive(datId, aceId);

            if (!IsKeywordsSearchEnabled) {
                KeywordsSearchTooltip = "Keywords not generated for this project. Search is restricted to file IDs.";
            }
            else if (!IsEmbeddingSearchActive) {
                KeywordsSearchTooltip = "Semantic search models not loaded. Only keyword search is available.";
            }
            else {
                KeywordsSearchTooltip = "Search by keyword or semantic meaning.";
            }
        }

        public void Dispose() {
            if (_isDisposed) return;
            _isDisposed = true;

            _searchCts?.Cancel();
            _searchCts?.Dispose();

            _keywordRepository.GlobalProgress -= OnKeywordGenerationProgress;
            _projectManager.CurrentProjectChanged -= OnProjectChanged;
            _objTool.PropertyChanged -= OnToolPropertyChanged;

            GridBrowser?.Dispose();
        }
    }
}
