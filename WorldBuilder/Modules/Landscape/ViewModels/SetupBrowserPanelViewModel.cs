using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Modules.DatBrowser.ViewModels;
using WorldBuilder.Services;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Services;
using System.ComponentModel;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Landscape.ViewModels {
    public partial class SetupBrowserPanelViewModel : CommonSetupBrowserViewModel {
        private readonly ObjectManipulationTool _objTool;

        public bool IsObjectToolActive => _objTool.IsActive;

        public SetupBrowserPanelViewModel(
            IKeywordRepositoryService keywordRepository,
            ProjectManager projectManager,
            IDatReaderWriter dats,
            WorldBuilderSettings settings,
            ThemeService themeService,
            ObjectManipulationTool objTool) 
            : base(dats, settings, themeService, keywordRepository, projectManager) {
            _objTool = objTool;
            _objTool.PropertyChanged += OnToolPropertyChanged;

            if (GridBrowser != null) {
                GridBrowser.ShowToolbar = false;
            }

            UpdateSearchState();
        }

        private void OnToolPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(ObjectManipulationTool.IsActive)) {
                OnPropertyChanged(nameof(IsObjectToolActive));
            }
        }

        protected override void OnObjectLoaded(Setup? obj) {
            if (obj != null) {
                _objTool.EnterPlacementMode(obj.Id);
            }
        }

        public override void Dispose() {
            base.Dispose();
            _objTool.PropertyChanged -= OnToolPropertyChanged;
        }
    }
}
