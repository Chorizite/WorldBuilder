using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class SetupBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.Setup>, IKeywordSearchViewModel {
        private readonly IKeywordRepositoryService _keywordRepository;
        private readonly ProjectManager _projectManager;
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

        public IEnumerable<SearchType> SearchTypes => System.Enum.GetValues<SearchType>();

        public SetupBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService, IKeywordRepositoryService keywordRepository, ProjectManager projectManager) : base(DBObjType.Setup, dats, settings, themeService) {
            _keywordRepository = keywordRepository;
            _projectManager = projectManager;

            UpdateSearchState();
            _projectManager.CurrentProjectChanged += OnCurrentProjectChanged;
            _keywordRepository.GlobalProgress += OnKeywordGenerationProgress;

            GridBrowser.PropertyChanged += OnGridBrowserPropertyChanged;
        }

        private void OnCurrentProjectChanged(object? sender, System.EventArgs e) {
            UpdateSearchState();
        }

        private void OnGridBrowserPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(GridBrowserViewModel.KeywordsSearchText)) {
                if (KeywordsSearchText != GridBrowser.KeywordsSearchText) {
                    KeywordsSearchText = GridBrowser.KeywordsSearchText;
                }
            }
        }

        partial void OnSearchTypeChanged(SearchType value) {
            PerformSearch(KeywordsSearchText);
        }

        private void OnKeywordGenerationProgress(object? sender, IKeywordRepositoryService.KeywordGenerationProgress e) {
            if (_isDisposed) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                if (_isDisposed) return;
                UpdateSearchState();
            });
        }

        private void UpdateSearchState() {
            if (_isDisposed) return;
            var project = _projectManager.CurrentProject;
            bool enabled = true;
            bool isEmbeddingSearch = false;
            string tooltip = "Search by keywords (e.g., class name, string properties)";
            string watermark = "Search keywords...";

            if (project == null || !project.ManagedIds.ManagedDatSetId.HasValue || !project.ManagedIds.ManagedAceDbId.HasValue) {
                enabled = false;
                tooltip = "Associate an ACE world database in project settings to enable keyword search.";
            }
            else if (!_keywordRepository.CanSearchKeywords(project.ManagedIds.ManagedDatSetId.Value, project.ManagedIds.ManagedAceDbId.Value)) {
                enabled = false;
                tooltip = "Keywords database is not generated or out of date.";
            }
            else {
                isEmbeddingSearch = _keywordRepository.IsEmbeddingSearchActive(project.ManagedIds.ManagedDatSetId.Value, project.ManagedIds.ManagedAceDbId.Value);
                if (isEmbeddingSearch) {
                    tooltip = "Semantic search active. You can search by meaning (e.g., 'halloween' or 'antarctica').";
                    watermark = "Semantic search (e.g., 'halloween')...";
                }
                else {
                    var db = _keywordRepository.GetManagedKeywordDb(project.ManagedIds.ManagedDatSetId.Value, project.ManagedIds.ManagedAceDbId.Value);
                    if (db != null && db.NameEmbeddingProgress >= 1f && db.DescEmbeddingProgress >= 1f) {
                        tooltip = "Embeddings generated, but semantic search is inactive (possibly missing model files). Try regenerating with 'Force'.";
                    }
                    else {
                        tooltip = "Search by keywords (e.g., class name, string properties). Consider generating embeddings for semantic search.";
                    }
                    watermark = "Search keywords...";
                }
            }

            IsKeywordsSearchEnabled = enabled;
            KeywordsSearchTooltip = tooltip;
            IsEmbeddingSearchActive = isEmbeddingSearch;

            if (!isEmbeddingSearch) {
                SearchType = SearchType.Keyword;
            }

            if (GridBrowser != null) {
                GridBrowser.IsKeywordsSearchEnabled = enabled;
                GridBrowser.KeywordsSearchTooltip = tooltip;
                GridBrowser.KeywordsSearchWatermark = watermark;
                GridBrowser.IsEmbeddingSearchActive = isEmbeddingSearch;
            }
        }

        partial void OnKeywordsSearchTextChanged(string value) {
            GridBrowser.KeywordsSearchText = value;
            PerformSearch(value);
        }

        private void PerformSearch(string value) {
            if (_isDisposed) return;

            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                if (_isDisposed) return;
                GridBrowser.IsKeywordsSearching = true;
            });

            Task.Run(async () => {
                try {
                    // Debounce search
                    await Task.Delay(300, ct);

                    IEnumerable<uint>? filteredIds = null;
                    if (!string.IsNullOrWhiteSpace(value) && IsKeywordsSearchEnabled) {
                        var project = _projectManager.CurrentProject;
                        if (project != null && project.ManagedIds.ManagedDatSetId.HasValue && project.ManagedIds.ManagedAceDbId.HasValue) {
                            var matchingIds = await _keywordRepository.SearchSetupsAsync(project.ManagedIds.ManagedDatSetId.Value, project.ManagedIds.ManagedAceDbId.Value, value, SearchType, ct);
                            filteredIds = matchingIds;
                        }
                    }

                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        if (!ct.IsCancellationRequested && !_isDisposed) {
                            GridBrowser.SetFileIds(filteredIds ?? _database.GetAllIdsOfType<DatReaderWriter.DBObjs.Setup>().OrderBy(x => x).ToList());
                            GridBrowser.IsKeywordsSearching = false;
                        }
                    });
                }
                catch (TaskCanceledException) { }
                catch (System.OperationCanceledException) { }
                catch (System.Exception) {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        if (!ct.IsCancellationRequested && !_isDisposed) {
                            GridBrowser.IsKeywordsSearching = false;
                        }
                    });
                }
            }, ct);
        }

        public override void Dispose() {
            if (_isDisposed) return;
            _isDisposed = true;

            base.Dispose();
            _projectManager.CurrentProjectChanged -= OnCurrentProjectChanged;
            _keywordRepository.GlobalProgress -= OnKeywordGenerationProgress;
            GridBrowser.PropertyChanged -= OnGridBrowserPropertyChanged;

            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;
        }
    }
}
