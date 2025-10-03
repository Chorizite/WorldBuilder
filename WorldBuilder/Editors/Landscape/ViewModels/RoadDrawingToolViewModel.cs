using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Tools;

namespace WorldBuilder.Editors.Landscape.ViewModels {
    public partial class RoadDrawingToolViewModel : ToolViewModelBase {
        public override string Name => "Road";
        public override string IconGlyph => "🛣️";

        [ObservableProperty]
        private ObservableCollection<SubToolViewModelBase> _subTools = new();

        public override ObservableCollection<SubToolViewModelBase> AllSubTools => SubTools;

        public RoadDrawingToolViewModel(
            RoadPointSubToolViewModel pointSubTool,
            RoadLineSubToolViewModel lineSubTool,
            RoadRemoveSubToolViewModel removeSubTool) {

            SubTools.Add(pointSubTool);
            SubTools.Add(lineSubTool);
            SubTools.Add(removeSubTool);
        }

        public override void OnActivated() {
            SelectedSubTool?.OnActivated();
        }

        public override void OnDeactivated() {
            SelectedSubTool?.OnDeactivated();
        }

        public override bool HandleMouseDown(MouseState mouseState) {
            return SelectedSubTool?.HandleMouseDown(mouseState) ?? false;
        }

        public override bool HandleMouseUp(MouseState mouseState) {
            return SelectedSubTool?.HandleMouseUp(mouseState) ?? false;
        }

        public override bool HandleMouseMove(MouseState mouseState) {
            return SelectedSubTool?.HandleMouseMove(mouseState) ?? false;
        }

        public override void Update(double deltaTime) {
            SelectedSubTool?.Update(deltaTime);
        }
    }
}