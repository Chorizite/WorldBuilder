using WorldBuilder.Lib.Settings;
using WorldBuilder.Shared.Modules.Landscape.Tools;

namespace WorldBuilder.Modules.Landscape {
    /// <summary>
    /// Implementation of IToolSettingsProvider that provides access to project settings
    /// </summary>
    public class ToolSettingsProvider : IToolSettingsProvider {
        private readonly ProjectSettings _projectSettings;

        public ToolSettingsProvider(ProjectSettings projectSettings) {
            _projectSettings = projectSettings;
        }

        public BrushToolSettingsData? BrushToolSettings => _projectSettings?.LandscapeTools?.BrushTool != null 
            ? new BrushToolSettingsData {
                BrushSize = _projectSettings.LandscapeTools.BrushTool.BrushSize,
                Texture = _projectSettings.LandscapeTools.BrushTool.Texture,
                SelectedScenery = _projectSettings.LandscapeTools.BrushTool.SelectedScenery
            }
            : null;

        public BucketFillToolSettingsData? BucketFillToolSettings => _projectSettings?.LandscapeTools?.BucketFillTool != null
            ? new BucketFillToolSettingsData {
                IsContiguous = _projectSettings.LandscapeTools.BucketFillTool.IsContiguous,
                OnlyFillSameScenery = _projectSettings.LandscapeTools.BucketFillTool.OnlyFillSameScenery,
                Texture = _projectSettings.LandscapeTools.BucketFillTool.Texture,
                SelectedScenery = _projectSettings.LandscapeTools.BucketFillTool.SelectedScenery
            }
            : null;

        public ObjectManipulationToolSettingsData? ObjectManipulationToolSettings => _projectSettings?.LandscapeTools?.ObjectManipulationTool != null
            ? new ObjectManipulationToolSettingsData {
                AlignToSurface = _projectSettings.LandscapeTools.ObjectManipulationTool.AlignToSurface,
                ShowBoundingBoxes = _projectSettings.LandscapeTools.ObjectManipulationTool.ShowBoundingBoxes,
                IsLocalSpace = _projectSettings.LandscapeTools.ObjectManipulationTool.IsLocalSpace,
                Mode = _projectSettings.LandscapeTools.ObjectManipulationTool.Mode
            }
            : null;

        public InspectorToolSettingsData? InspectorToolSettings => _projectSettings?.LandscapeTools?.InspectorTool != null
            ? new InspectorToolSettingsData {
                SelectVertices = _projectSettings.LandscapeTools.InspectorTool.SelectVertices,
                SelectBuildings = _projectSettings.LandscapeTools.InspectorTool.SelectBuildings,
                SelectStaticObjects = _projectSettings.LandscapeTools.InspectorTool.SelectStaticObjects,
                SelectScenery = _projectSettings.LandscapeTools.InspectorTool.SelectScenery,
                SelectEnvCells = _projectSettings.LandscapeTools.InspectorTool.SelectEnvCells,
                SelectEnvCellStaticObjects = _projectSettings.LandscapeTools.InspectorTool.SelectEnvCellStaticObjects,
                SelectPortals = _projectSettings.LandscapeTools.InspectorTool.SelectPortals,
                ShowBoundingBoxes = _projectSettings.LandscapeTools.InspectorTool.ShowBoundingBoxes
            }
            : null;

        // Methods to update the actual project settings from the data structures
        public void UpdateBrushToolSettings(BrushToolSettingsData data) {
            if (_projectSettings?.LandscapeTools?.BrushTool != null) {
                _projectSettings.LandscapeTools.BrushTool.BrushSize = data.BrushSize;
                _projectSettings.LandscapeTools.BrushTool.Texture = data.Texture;
                _projectSettings.LandscapeTools.BrushTool.SelectedScenery = data.SelectedScenery;
            }
        }

        public void UpdateBucketFillToolSettings(BucketFillToolSettingsData data) {
            if (_projectSettings?.LandscapeTools?.BucketFillTool != null) {
                _projectSettings.LandscapeTools.BucketFillTool.IsContiguous = data.IsContiguous;
                _projectSettings.LandscapeTools.BucketFillTool.OnlyFillSameScenery = data.OnlyFillSameScenery;
                _projectSettings.LandscapeTools.BucketFillTool.Texture = data.Texture;
                _projectSettings.LandscapeTools.BucketFillTool.SelectedScenery = data.SelectedScenery;
            }
        }

        public void UpdateObjectManipulationToolSettings(ObjectManipulationToolSettingsData data) {
            if (_projectSettings.LandscapeTools?.ObjectManipulationTool != null) {
                _projectSettings.LandscapeTools.ObjectManipulationTool.AlignToSurface = data.AlignToSurface;
                _projectSettings.LandscapeTools.ObjectManipulationTool.ShowBoundingBoxes = data.ShowBoundingBoxes;
                _projectSettings.LandscapeTools.ObjectManipulationTool.IsLocalSpace = data.IsLocalSpace;
                _projectSettings.LandscapeTools.ObjectManipulationTool.Mode = data.Mode;
            }
        }

        public void UpdateInspectorToolSettings(InspectorToolSettingsData data) {
            if (_projectSettings.LandscapeTools?.InspectorTool != null) {
                _projectSettings.LandscapeTools.InspectorTool.SelectVertices = data.SelectVertices;
                _projectSettings.LandscapeTools.InspectorTool.SelectBuildings = data.SelectBuildings;
                _projectSettings.LandscapeTools.InspectorTool.SelectStaticObjects = data.SelectStaticObjects;
                _projectSettings.LandscapeTools.InspectorTool.SelectScenery = data.SelectScenery;
                _projectSettings.LandscapeTools.InspectorTool.SelectEnvCells = data.SelectEnvCells;
                _projectSettings.LandscapeTools.InspectorTool.SelectEnvCellStaticObjects = data.SelectEnvCellStaticObjects;
                _projectSettings.LandscapeTools.InspectorTool.SelectPortals = data.SelectPortals;
                _projectSettings.LandscapeTools.InspectorTool.ShowBoundingBoxes = data.ShowBoundingBoxes;
            }
        }
    }
}
