using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using WorldBuilder.Shared.Documents;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Editors.Landscape.ViewModels {
    public partial class LayerTreeItemViewModel : ViewModelBase {
        public TerrainLayerBase Model { get; }
        public LayersViewModel Owner { get; }
        public LayerTreeItemViewModel? Parent { get; }
        public bool IsBase { get; set; }

        [ObservableProperty] private string _name;

        [ObservableProperty] private bool _isVisible;

        [ObservableProperty] private bool _isExport;

        [ObservableProperty] private bool _isExpanded = true;

        public bool IsLayer => Model is TerrainLayer;
        public bool IsGroup => Model is TerrainLayerGroup;

        public ObservableCollection<LayerTreeItemViewModel> Children { get; } = new();

        public LayerTreeItemViewModel(TerrainLayerBase model, LayerTreeItemViewModel? parent, LayersViewModel owner) {
            Model = model;
            Parent = parent;
            Owner = owner;
            _name = model.Name;
            _isVisible = model.IsVisible;
            _isExport = model.IsExport;
        }
    }
}