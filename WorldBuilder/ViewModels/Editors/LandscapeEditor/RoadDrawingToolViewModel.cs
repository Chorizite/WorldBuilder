using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Tools.Landscape;

namespace WorldBuilder.ViewModels.Editors.LandscapeEditor {
    public partial class RoadDrawingToolViewModel : ToolViewModelBase {
        public override string Name => "Roads";
        public override string IconGlyph => "🛣️";

        [ObservableProperty]
        private ObservableCollection<SubToolViewModelBase> _subTools = new();
        public override ObservableCollection<SubToolViewModelBase> AllSubTools => SubTools;

        public RoadDrawingToolViewModel() {
            // Add your road subtools here
            SubTools.Add(new RoadDrawSubToolViewModel());
            SubTools.Add(new RoadEditSubToolViewModel());
            SubTools.Add(new RoadEraseSubToolViewModel());
            SelectSubTool(SubTools[0]);
        }

        public override ITerrainTool CreateTool() {
            return new RoadDrawingTool();
        }
    }
    // Placeholder subtools - implement these based on your needs
    public partial class RoadDrawSubToolViewModel : SubToolViewModelBase {
        public override string Name => "Draw";
        public override string IconGlyph => "✏️";
    }

    public partial class RoadEditSubToolViewModel : SubToolViewModelBase {
        public override string Name => "Line";
        public override string IconGlyph => "📈";
    }

    public partial class RoadEraseSubToolViewModel : SubToolViewModelBase {
        public override string Name => "Erase";
        public override string IconGlyph => "🗑️";
    }
}