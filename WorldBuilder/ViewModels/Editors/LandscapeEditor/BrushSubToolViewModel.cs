using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.Enums;
using System.Collections.Generic;
using System.Linq;

namespace WorldBuilder.ViewModels.Editors.LandscapeEditor {
    public partial class BrushSubToolViewModel : SubToolViewModelBase {
        public override string Name => "Brush";
        public override string IconGlyph => "🖌️";

        [ObservableProperty]
        private float _brushRadius = 5f;

        [ObservableProperty]
        private TerrainTextureType _selectedTerrainType = TerrainTextureType.Volcano1;

        [ObservableProperty]
        private List<TerrainTextureType> _availableTerrainTypes;

        public BrushSubToolViewModel() {
            _availableTerrainTypes = System.Enum.GetValues<TerrainTextureType>().ToList();
        }

        partial void OnBrushRadiusChanged(float value) {
            // Update the actual tool settings
            if (value < 0.5f) BrushRadius = 0.5f;
            if (value > 50f) BrushRadius = 50f;
        }
    }
}