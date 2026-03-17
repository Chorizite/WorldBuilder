using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;
using WorldBuilder.Lib.Settings;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public abstract partial class CommonSetupBrowserViewModel : BaseDatBrowserViewModel<Setup>, IKeywordSearchViewModel {
        protected readonly IKeywordRepositoryService _keywordRepository;
        protected readonly ProjectManager _projectManager;
        private CancellationTokenSource? _searchCts;
        private bool _isDisposed;
        private WorldBuilder.Shared.Models.IProject? _currentProject;

        protected CommonSetupBrowserViewModel(
            IDatReaderWriter dats, 
            WorldBuilderSettings settings, 
            ThemeService themeService, 
            IKeywordRepositoryService keywordRepository, 
            ProjectManager projectManager) 
            : base(DBObjType.Setup, dats, settings, themeService) {
            _keywordRepository = keywordRepository;
            _projectManager = projectManager;

            _projectManager.CurrentProjectChanged += OnCurrentProjectChangedInternal;
            _currentProject = _projectManager.CurrentProject;
            if (_currentProject != null) {
                _currentProject.ManagedAceDbIdChanged += OnManagedAceDbIdChangedInternal;
            }
            _keywordRepository.GlobalProgress += OnKeywordGenerationProgressInternal;

            GridBrowser.PropertyChanged += OnGridBrowserPropertyChangedInternal;
            
            SearchType = SearchType.Hybrid;
        }

        private void OnCurrentProjectChangedInternal(object? sender, EventArgs e) {
            if (_currentProject != null) {
                _currentProject.ManagedAceDbIdChanged -= OnManagedAceDbIdChangedInternal;
            }
            _currentProject = _projectManager.CurrentProject;
            if (_currentProject != null) {
                _currentProject.ManagedAceDbIdChanged += OnManagedAceDbIdChangedInternal;
            }
            OnProjectChanged();
            UpdateSearchState();
        }

        protected virtual void OnProjectChanged() {
            KeywordsSearchText = string.Empty;
        }

        private void OnManagedAceDbIdChangedInternal(object? sender, EventArgs e) {
            UpdateSearchState();
        }

        private void OnKeywordGenerationProgressInternal(object? sender, IKeywordRepositoryService.KeywordGenerationProgress e) {
            if (_isDisposed) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                if (_isDisposed) return;
                UpdateSearchState();
            });
        }

        private void OnGridBrowserPropertyChangedInternal(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(GridBrowserViewModel.KeywordsSearchText)) {
                if (KeywordsSearchText != GridBrowser.KeywordsSearchText) {
                    KeywordsSearchText = GridBrowser.KeywordsSearchText;
                }
            }
        }

        protected override void OnSearchTypeChangedInternal(SearchType value) {
            PerformSearch(KeywordsSearchText);
        }

        protected override void OnKeywordsSearchTextChangedInternal(string value) {
            GridBrowser.KeywordsSearchText = value;
            PerformSearch(value);
        }

        protected void UpdateSearchState() {
            if (_isDisposed) return;
            var project = _projectManager.CurrentProject;
            bool showWarning = false;
            bool isEmbeddingSearch = false;
            string tooltip = "Search by keywords, semantic meaning, or file ID.";
            string watermark = "Search keywords or ID...";

            var datId = project?.ManagedIds.ManagedDatSetId ?? Guid.Empty;
            var aceId = project?.ManagedIds.ManagedAceDbId ?? Guid.Empty;

            if (project == null) {
                tooltip = "No project loaded. Search is restricted to file IDs.";
            }
            else if (aceId == Guid.Empty) {
                showWarning = true;
                tooltip = "No ACE database associated. Open Settings -> Project -> Server to add a database. Search is restricted to file IDs.";
            }
            else if (!_keywordRepository.CanSearchKeywords(datId, aceId)) {
                showWarning = true;
                tooltip = "Keywords database is not generated or out of date. Search is restricted to file IDs.";
            }
            else {
                isEmbeddingSearch = _keywordRepository.IsEmbeddingSearchActive(datId, aceId);
                if (isEmbeddingSearch) {
                    tooltip = "Semantic search active. You can search by meaning (e.g., 'halloween') or file ID.";
                    watermark = "Semantic search or ID...";
                }
                else {
                    var db = _keywordRepository.GetManagedKeywordDb(datId, aceId);
                    if (db != null && db.NameEmbeddingProgress >= 1f && db.DescEmbeddingProgress >= 1f) {
                        tooltip = "Embeddings generated, but semantic search is inactive. Search by keywords or file ID.";
                    }
                    else {
                        tooltip = "Search by keywords or file ID. Consider generating embeddings for semantic search.";
                    }
                    watermark = "Search keywords or ID...";
                }
            }

            IsKeywordsSearchEnabled = true; // Always enable the box
            ShowKeywordsSearchWarning = showWarning;
            KeywordsSearchTooltip = tooltip;
            IsEmbeddingSearchActive = isEmbeddingSearch;
            IsSearchTypeSelectionEnabled = !showWarning && project != null;

            if (GridBrowser != null) {
                GridBrowser.IsKeywordsSearchEnabled = true;
                GridBrowser.ShowKeywordsSearchWarning = showWarning;
                GridBrowser.KeywordsSearchTooltip = tooltip;
                GridBrowser.KeywordsSearchWatermark = watermark;
                GridBrowser.IsEmbeddingSearchActive = isEmbeddingSearch;
            }
        }

        protected void PerformSearch(string value) {
            if (_isDisposed) return;

            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                if (_isDisposed) return;
                GridBrowser.IsKeywordsSearching = !string.IsNullOrWhiteSpace(value);
            });

            Task.Run(async () => {
                try {
                    await Task.Delay(300, ct);

                    List<uint> results = new();

                    if (!string.IsNullOrWhiteSpace(value)) {
                        if (TryParseId(value, out uint id)) {
                            if (_database.TryGet<Setup>(id, out _)) {
                                results.Add(id);
                            }
                        }

                        var project = _projectManager.CurrentProject;
                        var datId = project?.ManagedIds.ManagedDatSetId ?? Guid.Empty;
                        var aceId = project?.ManagedIds.ManagedAceDbId ?? Guid.Empty;

                        if (_keywordRepository.AreKeywordsValid(datId, aceId)) {
                            var matchingIds = await _keywordRepository.SearchSetupsAsync(datId, aceId, value, SearchType, ct);
                            if (matchingIds != null) {
                                foreach (var resId in matchingIds) {
                                    if (!results.Contains(resId)) {
                                        results.Add(resId);
                                    }
                                }
                            }
                        }
                    }

                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        if (!ct.IsCancellationRequested && !_isDisposed) {
                            GridBrowser.SetFileIds(string.IsNullOrWhiteSpace(value) ? _database.GetAllIdsOfType<Setup>().OrderBy(x => x).ToList() : results);
                            GridBrowser.IsKeywordsSearching = false;
                        }
                    });
                }
                catch (OperationCanceledException) { }
                catch (Exception) {
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
            _projectManager.CurrentProjectChanged -= OnCurrentProjectChangedInternal;
            if (_currentProject != null) {
                _currentProject.ManagedAceDbIdChanged -= OnManagedAceDbIdChangedInternal;
            }
            _keywordRepository.GlobalProgress -= OnKeywordGenerationProgressInternal;
            GridBrowser.PropertyChanged -= OnGridBrowserPropertyChangedInternal;

            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;
        }
    }
}
