using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.ViewModels.Editors.LandscapeEditor;

namespace WorldBuilder.ViewModels.Editors {
    public partial class LandscapeEditorViewModel : ViewModelBase {
        [ObservableProperty]
        private ObservableCollection<ToolViewModelBase> _tools = new();

        [ObservableProperty]
        private ToolViewModelBase? _selectedTool;

        public LandscapeEditorViewModel() {
            Tools.Add(new TexturePaintingToolViewModel());
            Tools.Add(new RoadDrawingToolViewModel());
            SelectTool(Tools[0]);
        }

        [RelayCommand]
        private void SelectTool(ToolViewModelBase tool) {
            if (SelectedTool != null) {
                SelectedTool.IsSelected = false;
            }
            SelectedTool = tool;
            SelectedTool.IsSelected = true;
        }
    }
}
