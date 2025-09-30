using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Tools.Landscape;

namespace WorldBuilder.ViewModels.Editors.LandscapeEditor {
    public partial class TexturePaintingToolViewModel : ToolViewModelBase {
        public override string Name => "Terrain";
        public override string IconGlyph => "🖌️";

        [ObservableProperty]
        private ObservableCollection<SubToolViewModelBase> _subTools = new();

        public override ObservableCollection<SubToolViewModelBase> AllSubTools => SubTools;

        public TexturePaintingToolViewModel() {
            SubTools.Add(new BrushSubToolViewModel());
            SubTools.Add(new BucketFillSubToolViewModel());
            SelectSubTool(SubTools[0]);
        }

        public override ITerrainTool CreateTool() {
            return new TexturePaintingTool();
        }
    }
}
