using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.Enums;
using System.Collections.Generic;
using System.Linq;

namespace WorldBuilder.ViewModels.Editors.LandscapeEditor {
    public partial class BucketFillSubToolViewModel : SubToolViewModelBase {
        public override string Name => "Bucket Fill";
        public override string IconGlyph => "🪣";

        [ObservableProperty]
        private TerrainTextureType _selectedTerrainType = TerrainTextureType.Volcano1;

        [ObservableProperty]
        private List<TerrainTextureType> _availableTerrainTypes;

        public BucketFillSubToolViewModel() {
            _availableTerrainTypes = System.Enum.GetValues<TerrainTextureType>().ToList();
        }
    }
}