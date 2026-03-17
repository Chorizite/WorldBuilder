using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class SetupBrowserViewModel : CommonSetupBrowserViewModel {
        public SetupBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService, IKeywordRepositoryService keywordRepository, ProjectManager projectManager) 
            : base(dats, settings, themeService, keywordRepository, projectManager) {
            UpdateSearchState();
        }
    }
}
