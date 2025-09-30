using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Tools.Landscape;

namespace WorldBuilder.ViewModels.Editors.LandscapeEditor {
    public abstract partial class ToolViewModelBase : ViewModelBase {
        public abstract string Name { get; }
        public abstract string IconGlyph { get; }

        [ObservableProperty]
        public bool _isSelected = false;
        public abstract ObservableCollection<SubToolViewModelBase> AllSubTools { get; }

        [ObservableProperty]
        private SubToolViewModelBase? _selectedSubTool;

        public abstract ITerrainTool CreateTool();

        [RelayCommand]
        public void SelectSubTool(SubToolViewModelBase subTool) {
            if (SelectedSubTool != null) {
                SelectedSubTool.IsSelected = false;
            }

            SelectedSubTool = subTool;
            SelectedSubTool.IsSelected = true;
        }
    }
}
