using System.Collections.Generic;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public interface IKeywordSearchViewModel {
        string KeywordsSearchText { get; set; }
        bool IsKeywordsSearchEnabled { get; }
        string KeywordsSearchTooltip { get; }
        bool IsEmbeddingSearchActive { get; }
        SearchType SearchType { get; set; }
        IEnumerable<SearchType> SearchTypes { get; }
        GridBrowserViewModel? GridBrowser { get; }
    }
}
