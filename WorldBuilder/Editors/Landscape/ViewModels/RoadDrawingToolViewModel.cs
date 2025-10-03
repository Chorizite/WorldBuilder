using Chorizite.Core.Render;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Lib;

namespace WorldBuilder.Editors.Landscape.ViewModels {
    public partial class RoadDrawingToolViewModel : ToolViewModelBase {
        public override string Name => "Roads";
        public override string IconGlyph => "🛣️";

        [ObservableProperty]
        private ObservableCollection<SubToolViewModelBase> _subTools = new();
        public override ObservableCollection<SubToolViewModelBase> AllSubTools => SubTools;

        public RoadDrawingToolViewModel(RoadDrawSubToolViewModel roadDrawSubTool, RoadEditSubToolViewModel roadEditSubTool, RoadEraseSubToolViewModel roadEraseSubTool) {
            // Add your road subtools here
            SubTools.Add(roadDrawSubTool);
            SubTools.Add(roadEditSubTool);
            SubTools.Add(roadEraseSubTool);
        }
        public override void OnActivated() {
            // Initialize road drawing tool
        }

        public override void OnDeactivated() {
            // Clean up road drawing tool
        }

        public override bool HandleMouseDown(MouseState mouseState) {
            // Implement road drawing mouse down logic
            return false;
        }

        public override bool HandleMouseUp(MouseState mouseState) {
            // Implement road drawing mouse up logic
            return false;
        }

        public override bool HandleMouseMove(MouseState mouseState) {
            // Implement road drawing mouse move logic
            return false;
        }

        public override void Update(double deltaTime) {
            // Update road drawing tool
        }

        public override void RenderOverlay(IRenderer renderer, ICamera camera, float aspectRatio) {
            // Render road drawing overlay
        }
    }
    // Placeholder subtools - implement these based on your needs
    public partial class RoadDrawSubToolViewModel : SubToolViewModelBase {
        public override string Name => "Draw";
        public override string IconGlyph => "✏️";

        public RoadDrawSubToolViewModel(TerrainEditingContext context) : base(context) {

        }

        public override void OnActivated() {

        }

        public override void OnDeactivated() {

        }

        public override bool HandleMouseDown(MouseState mouseState) {
            return false;
        }

        public override bool HandleMouseMove(MouseState mouseState) {
            return false;
        }

        public override bool HandleMouseUp(MouseState mouseState) {
            return false;
        }
    }

    public partial class RoadEditSubToolViewModel : SubToolViewModelBase {
        public override string Name => "Line";
        public override string IconGlyph => "📈";

        public RoadEditSubToolViewModel(TerrainEditingContext context) : base(context) {

        }

        public override void OnActivated() {

        }

        public override void OnDeactivated() {

        }

        public override bool HandleMouseDown(MouseState mouseState) {
            return false;
        }

        public override bool HandleMouseMove(MouseState mouseState) {
            return false;
        }

        public override bool HandleMouseUp(MouseState mouseState) {
            return false;
        }
    }

    public partial class RoadEraseSubToolViewModel : SubToolViewModelBase {
        public override string Name => "Erase";
        public override string IconGlyph => "🗑️";

        public RoadEraseSubToolViewModel(TerrainEditingContext context) : base(context) {
        
        }

        public override void OnActivated() {

        }

        public override void OnDeactivated() {

        }

        public override bool HandleMouseDown(MouseState mouseState) {
            return false;
        }

        public override bool HandleMouseMove(MouseState mouseState) {
            return false;
        }

        public override bool HandleMouseUp(MouseState mouseState) {
            return false;
        }
    }
}