using System.Collections.Generic;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public interface IKeywordSearchViewModel {
        string KeywordsSearchText { get; set; }
        bool IsKeywordsSearchEnabled { get; }
        bool ShowKeywordsSearchWarning { get; }
        string KeywordsSearchTooltip { get; }
        bool IsEmbeddingSearchActive { get; }
        bool IsSearchTypeSelectionEnabled { get; }
        SearchType SearchType { get; set; }
        IEnumerable<SearchType> SearchTypes { get; }
        GridBrowserViewModel? GridBrowser { get; }
    }
}
