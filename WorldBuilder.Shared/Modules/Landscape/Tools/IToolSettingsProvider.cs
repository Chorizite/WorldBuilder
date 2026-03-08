namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    /// <summary>
    /// Interface for providing tool settings access to landscape tools
    /// </summary>
    public interface IToolSettingsProvider {
        BrushToolSettingsData? BrushToolSettings { get; }
        BucketFillToolSettingsData? BucketFillToolSettings { get; }
        ObjectManipulationToolSettingsData? ObjectManipulationToolSettings { get; }
        InspectorToolSettingsData? InspectorToolSettings { get; }

        // Update methods to save changes back to project settings
        void UpdateBrushToolSettings(BrushToolSettingsData data);
        void UpdateBucketFillToolSettings(BucketFillToolSettingsData data);
        void UpdateObjectManipulationToolSettings(ObjectManipulationToolSettingsData data);
        void UpdateInspectorToolSettings(InspectorToolSettingsData data);
    }

    public class BrushToolSettingsData {
        public int BrushSize { get; set; } = 1;
        public int Texture { get; set; } = 0;
        public int SelectedScenery { get; set; } = 255;
    }

    public class BucketFillToolSettingsData {
        public bool IsContiguous { get; set; } = true;
        public bool OnlyFillSameScenery { get; set; } = false;
        public int Texture { get; set; } = 0;
        public int SelectedScenery { get; set; } = 255;
    }

    public class ObjectManipulationToolSettingsData {
        public bool AlignToSurface { get; set; } = false;
        public bool ShowBoundingBoxes { get; set; } = false;
        public bool IsLocalSpace { get; set; } = false;
    }

    public class InspectorToolSettingsData {
        public bool SelectVertices { get; set; } = false;
        public bool SelectBuildings { get; set; } = true;
        public bool SelectStaticObjects { get; set; } = true;
        public bool SelectScenery { get; set; } = false;
        public bool SelectEnvCells { get; set; } = true;
        public bool SelectEnvCellStaticObjects { get; set; } = true;
        public bool SelectPortals { get; set; } = true;
        public bool ShowBoundingBoxes { get; set; } = true;
    }
}
