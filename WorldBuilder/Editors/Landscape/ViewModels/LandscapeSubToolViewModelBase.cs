using Chorizite.Core.Render;
using CommunityToolkit.Mvvm.ComponentModel;
using WorldBuilder.Lib;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Editors.Landscape.ViewModels {
    public abstract partial class SubToolViewModelBase : ViewModelBase {
        public abstract string Name { get; }
        public abstract string IconGlyph { get; }
        public TerrainEditingContext Context { get; }

        [ObservableProperty]
        private bool _isSelected = false;

        public SubToolViewModelBase(TerrainEditingContext context) => Context = context;

        // Tool lifecycle methods
        public abstract void OnActivated();
        public abstract void OnDeactivated();

        // Mouse interaction methods
        public abstract bool HandleMouseDown(MouseState mouseState);
        public abstract bool HandleMouseUp(MouseState mouseState);
        public abstract bool HandleMouseMove(MouseState mouseState);

        public virtual void Update(double deltaTime) {
        
        }

        public virtual void RenderOverlay(IRenderer renderer, ICamera camera, float aspectRatio) {
            
        }
    }
}