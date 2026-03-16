using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class SetupBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.Setup> {
        private readonly IKeywordRepositoryService _keywordRepository;
        private readonly ProjectManager _projectManager;
        private CancellationTokenSource? _searchCts;

        [ObservableProperty]
        private string _keywordsSearchText = string.Empty;

        [ObservableProperty]
        private bool _isKeywordsSearchEnabled;

        [ObservableProperty]
        private string _keywordsSearchTooltip = string.Empty;

        public SetupBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService, IKeywordRepositoryService keywordRepository, ProjectManager projectManager) : base(DBObjType.Setup, dats, settings, themeService) {
            _keywordRepository = keywordRepository;
            _projectManager = projectManager;

            UpdateSearchState();
            _projectManager.CurrentProjectChanged += (s, e) => UpdateSearchState();

            GridBrowser.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(GridBrowserViewModel.KeywordsSearchText)) {
                    if (KeywordsSearchText != GridBrowser.KeywordsSearchText) {
                        KeywordsSearchText = GridBrowser.KeywordsSearchText;
                    }
                }
            };
        }

        private void UpdateSearchState() {
            var project = _projectManager.CurrentProject;
            bool enabled = true;
            string tooltip = "Search by keywords (e.g., class name, string properties)";

            if (project == null || !project.ManagedDatSetId.HasValue || !project.ManagedAceDbId.HasValue) {
                enabled = false;
                tooltip = "Associate an ACE world database in project settings to enable keyword search.";
            }
            else if (!_keywordRepository.AreKeywordsValid(project.ManagedDatSetId.Value, project.ManagedAceDbId.Value)) {
                enabled = false;
                tooltip = "Keywords database is not generated or out of date.";
            }

            IsKeywordsSearchEnabled = enabled;
            KeywordsSearchTooltip = tooltip;
            GridBrowser.IsKeywordsSearchEnabled = enabled;
            GridBrowser.KeywordsSearchTooltip = tooltip;
        }

        partial void OnKeywordsSearchTextChanged(string value) {
            GridBrowser.KeywordsSearchText = value;
            PerformSearch(value);
        }

        private void PerformSearch(string value) {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            Task.Run(async () => {
                try {
                    // Debounce search
                    await Task.Delay(300, ct);

                    IEnumerable<uint>? filteredIds = null;
                    if (!string.IsNullOrWhiteSpace(value) && IsKeywordsSearchEnabled) {
                        var project = _projectManager.CurrentProject;
                        if (project != null && project.ManagedDatSetId.HasValue && project.ManagedAceDbId.HasValue) {
                            var matchingIds = await _keywordRepository.SearchSetupsAsync(project.ManagedDatSetId.Value, project.ManagedAceDbId.Value, value, ct);
                            filteredIds = matchingIds;
                        }
                    }

                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        if (!ct.IsCancellationRequested) {
                            Initialize(filteredIds);
                        }
                    });
                }
                catch (TaskCanceledException) { }
            }, ct);
        }

        public override void Dispose() {
            base.Dispose();
            _searchCts?.Cancel();
            _searchCts?.Dispose();
        }
    }
}
