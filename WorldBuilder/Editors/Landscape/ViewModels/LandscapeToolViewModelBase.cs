using Chorizite.Core.Render;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Lib;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Editors.Landscape.ViewModels {
    public abstract partial class ToolViewModelBase : ViewModelBase {
        public abstract string Name { get; }
        public abstract string IconGlyph { get; }

        [ObservableProperty]
        private bool _isSelected = false;

        public abstract ObservableCollection<SubToolViewModelBase> AllSubTools { get; }

        [ObservableProperty]
        private SubToolViewModelBase? _selectedSubTool;

        // Tool lifecycle methods
        public abstract void OnActivated();
        public abstract void OnDeactivated();

        // Mouse interaction methods
        public abstract bool HandleMouseDown(MouseState mouseState);
        public abstract bool HandleMouseUp(MouseState mouseState);
        public abstract bool HandleMouseMove(MouseState mouseState);

        // Per-frame update
        public abstract void Update(double deltaTime);

        // Optional: Render overlay
        public virtual void RenderOverlay(IRenderer renderer, ICamera camera, float aspectRatio) {
            // Default implementation does nothing
        }

        [RelayCommand]
        public virtual void ActivateSubTool(SubToolViewModelBase subTool) {
            if (SelectedSubTool != null) {
                SelectedSubTool.IsSelected = false;
                SelectedSubTool.OnDeactivated();
            }
            SelectedSubTool = subTool;
            SelectedSubTool.IsSelected = true;
            SelectedSubTool.OnActivated();
        }
    }
}
