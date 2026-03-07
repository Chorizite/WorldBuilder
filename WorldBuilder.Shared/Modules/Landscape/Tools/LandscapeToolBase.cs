using CommunityToolkit.Mvvm.ComponentModel;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Modules.Landscape.Lib;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    /// <summary>
    /// Base class for landscape tools that provides common functionality like brush management.
    /// </summary>
    public abstract class LandscapeToolBase : ObservableObject, ILandscapeTool {
        /// <inheritdoc/>
        public abstract string Name { get; }
        /// <inheritdoc/>
        public abstract string IconGlyph { get; }
        
        private bool _isActive;
        /// <inheritdoc/>
        public bool IsActive {
            get => _isActive;
            protected set => SetProperty(ref _isActive, value);
        }

        protected ILandscapeBrush? _brush;
        /// <inheritdoc/>
        public virtual ILandscapeBrush? Brush {
            get => _brush;
            protected set => SetProperty(ref _brush, value);
        }

        protected LandscapeToolContext? Context;
        private bool _wasBrushShowingBeforeSuspension;

        /// <inheritdoc/>
        public virtual void Activate(LandscapeToolContext context) {
            Context = context;
            IsActive = true;
        }

        /// <inheritdoc/>
        public virtual void Deactivate() {
            IsActive = false;
            if (_brush != null) {
                _brush.IsVisible = false;
            }
            Context = null;
        }

        /// <inheritdoc/>
        public virtual void Suspend() {
            if (_brush != null) {
                _wasBrushShowingBeforeSuspension = _brush.IsVisible;
                _brush.IsVisible = false;
            }
        }

        /// <inheritdoc/>
        public virtual void Resume() {
            if (_brush != null) {
                _brush.IsVisible = _wasBrushShowingBeforeSuspension;
            }
        }

        /// <inheritdoc/>
        public virtual void Update(double deltaTime) {
        }

        /// <inheritdoc/>
        public virtual void Render(IDebugRenderer debugRenderer) {
        }

        /// <inheritdoc/>
        public abstract bool OnPointerPressed(ViewportInputEvent e);

        /// <inheritdoc/>
        public abstract bool OnPointerMoved(ViewportInputEvent e);

        /// <inheritdoc/>
        public virtual bool OnPointerReleased(ViewportInputEvent e) {
            return false;
        }

        /// <inheritdoc/>
        public virtual bool OnKeyDown(ViewportInputEvent e) => false;

        /// <inheritdoc/>
        public virtual bool OnKeyUp(ViewportInputEvent e) => false;

        protected TerrainRaycastHit Raycast(double x, double y) {
            if (Context == null || Context.Document.Region == null) return new TerrainRaycastHit();

            return TerrainRaycast.Raycast((float)x, (float)y, (int)Context.ViewportSize.X, (int)Context.ViewportSize.Y, Context.Camera, Context.Document.Region, Context.Document, Context.Logger);
        }
    }
}
