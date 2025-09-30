using CommunityToolkit.Mvvm.ComponentModel;

namespace WorldBuilder.ViewModels.Editors.LandscapeEditor {
    public abstract partial class SubToolViewModelBase : ViewModelBase {
        public abstract string Name { get; }
        public abstract string IconGlyph { get; }

        [ObservableProperty]
        private bool _isSelected = false;
    }
}