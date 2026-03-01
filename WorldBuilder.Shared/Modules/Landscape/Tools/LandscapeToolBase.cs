using CommunityToolkit.Mvvm.ComponentModel;
using System.Numerics;
using WorldBuilder.Shared.Models;
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

        private bool _showBrush;
        /// <inheritdoc/>
        public bool ShowBrush {
            get => _showBrush;
            protected set => SetProperty(ref _showBrush, value);
        }

        private Vector3 _brushPosition;
        /// <inheritdoc/>
        public Vector3 BrushPosition {
            get => _brushPosition;
            protected set => SetProperty(ref _brushPosition, value);
        }

        private float _brushRadius = 30f;
        /// <inheritdoc/>
        public float BrushRadius {
            get => _brushRadius;
            protected set => SetProperty(ref _brushRadius, value);
        }

        private BrushShape _brushShape = BrushShape.Circle;
        /// <inheritdoc/>
        public BrushShape BrushShape {
            get => _brushShape;
            protected set => SetProperty(ref _brushShape, value);
        }

        protected LandscapeToolContext? Context;

        /// <inheritdoc/>
        public virtual void Activate(LandscapeToolContext context) {
            Context = context;
            IsActive = true;
        }

        /// <inheritdoc/>
        public virtual void Deactivate() {
            IsActive = false;
            ShowBrush = false;
            Context = null;
        }

        /// <inheritdoc/>
        public virtual void Update(double deltaTime) {
        }

        /// <inheritdoc/>
        public abstract bool OnPointerPressed(ViewportInputEvent e);

        /// <inheritdoc/>
        public abstract bool OnPointerMoved(ViewportInputEvent e);

        /// <inheritdoc/>
        public virtual bool OnPointerReleased(ViewportInputEvent e) {
            return false;
        }

        protected TerrainRaycastHit Raycast(double x, double y) {
            if (Context == null || Context.Document.Region == null) return new TerrainRaycastHit();

            return TerrainRaycast.Raycast((float)x, (float)y, (int)Context.ViewportSize.X, (int)Context.ViewportSize.Y, Context.Camera, Context.Document.Region, Context.Document, Context.Logger);
        }
    }
}
