namespace WorldBuilder.Lib.Input {
    /// <summary>
    /// Enumeration of all input actions that can be bound
    /// </summary>
    public enum InputAction {
        None,
        
        // Camera Movement
        [DefaultKey("W", "", 1)]
        [Category("Camera Movement", 1)]
        MoveForward,

        [DefaultKey("S", "", 2)]
        [Category("Camera Movement", 1)]
        MoveBackward,

        [DefaultKey("A", "", 3)]
        [Category("Camera Movement", 1)]
        MoveLeft,

        [DefaultKey("D", "", 4)]
        [Category("Camera Movement", 1)]
        MoveRight,

        [DefaultKey("E", "", 5)]
        [Category("Camera Movement", 1)]
        MoveUp,

        [DefaultKey("Q", "", 6)]
        [Category("Camera Movement", 1)]
        MoveDown,

        [DefaultKey("Up", "", 7)]
        [Category("Camera Movement", 1)]
        TurnUp,

        [DefaultKey("Down", "", 8)]
        [Category("Camera Movement", 1)]
        TurnDown,

        [DefaultKey("Left", "", 9)]
        [Category("Camera Movement", 1)]
        TurnLeft,

        [DefaultKey("Right", "", 10)]
        [Category("Camera Movement", 1)]
        TurnRight,

        [DefaultKey("LeftShift", "", 11)]
        [Category("Camera Movement", 1)]
        SpeedModifier,

        // Camera Controls
        [DefaultKey("Tab", "", 1)]
        [Category("Camera Controls", 2)]
        ToggleCameraMode,

        [DefaultKey("Up", "Control", 2)]
        [Category("Camera Controls", 2)]
        IncreaseSpeed,

        [DefaultKey("Down", "Control", 3)]
        [Category("Camera Controls", 2)]
        DecreaseSpeed,

        [DefaultKey("R", "", 4)]
        [Category("Camera Controls", 2)]
        ResetCameraAngle,

        // Tool Controls
        [DefaultKey("T", "", 1)]
        [Category("Tool Controls", 3)]
        TranslateTool,

        [DefaultKey("R", "", 2)]
        [Category("Tool Controls", 3)]
        RotateTool,

        [DefaultKey("F", "", 3)]
        [Category("Tool Controls", 3)]
        BothTool,

        [DefaultKey("OemMinus", "", 4)]
        [Category("Tool Controls", 3)]
        DecreaseBrushSize,

        [DefaultKey("OemPlus", "", 5)]
        [Category("Tool Controls", 3)]
        IncreaseBrushSize,

        // Application Actions
        [DefaultKey("G", "Control", 1)]
        [Category("Application Actions", 4)]
        GoToLocation,

        [DefaultKey("B", "Control", 2)]
        [Category("Application Actions", 4)]
        AddBookmark,

        [DefaultKey("F", "Control", 3)]
        [Category("Application Actions", 4)]
        GoToFileId
    }
}
