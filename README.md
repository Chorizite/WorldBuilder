# WorldBuilder

A comprehensive terrain editing and world building tool for Asheron's Call, built with AvaloniaUI and OpenGL.

## Features

### Advanced Camera System
- **Dual Camera Modes**: Seamlessly switch between 3D perspective and 2D orthographic (flatmap) views
- **Smooth Transitions**: Animated camera transitions when switching modes automatically
- **Interactive 2D Rotation**: Rotate the flatmap view with middle mouse button
- **Smart Auto-Switching**: Automatically transitions between modes based on altitude/zoom level
- See [CAMERA_SPECIFICATION.md](CAMERA_SPECIFICATION.md) for detailed camera controls and technical documentation

### Terrain Editing Tools

**Height Sculpting:**
- **Raise/Lower**: Sculpt terrain height with adjustable brush size and intensity
  - Left-click: Raise terrain
  - Alt + Left-click: Lower terrain
- **Smooth**: Smooth out rough terrain transitions
- **Flatten**: Create flat surfaces at target height

**Terrain Painting:**
- **Primary/Alternative Types**: Work with two terrain types simultaneously
  - Left-click: Paint with primary terrain type
  - Alt + Left-click: Paint with alternative terrain type
  - X key: Quick-swap primary and alternative types

### Command History
- Full undo/redo support (Ctrl+Z / Ctrl+Y)
- Command batching for performance
- Separate undo stacks for different editing operations

## Quick Start

### Camera Controls

**3D Perspective Mode:**
- **WASD**: Move camera
- **Space/Shift**: Move up/down
- **Right-click + Drag**: Rotate view
- **Mouse Wheel**: Zoom in/out
- **Q**: Toggle to 2D mode

**2D Orthographic Mode:**
- **WASD**: Pan camera (rotation-aware)
- **Space/Shift**: Zoom out/in
- **Right-click + Drag**: Pan camera
- **Middle-click + Drag**: Rotate map
- **R**: Reset orientation to north
- **Q**: Toggle to 3D mode

### Settings

Access camera settings: **Settings → Landscape Editor → Camera**
- Max Draw Distance (100-100000, default: 4000)
- Field of View (30-120°, default: 60°)
- Mouse Sensitivity (0.1-5.0, default: 1.0)
- Mouse Wheel Zoom Sensitivity (0.1-3.0, default: 1.0)
- Movement Speed (1-20000, default: 1000)

## Documentation

- [Camera System Specification](CAMERA_SPECIFICATION.md) - Comprehensive technical documentation for the camera system
- [Rendering Optimization Plan](docs/RENDERING_OPTIMIZATION_PLAN.md) - Plan for texture-based rendering optimization (Issue #3)
- [Project Specifications](docs/) - Project architecture and feature specifications

## Development

### Technologies
- **UI Framework**: AvaloniaUI
- **Graphics**: OpenGL (via Silk.NET)
- **Backend**: Chorizite.Core

### Building
```bash
# Clone the repository
git clone https://github.com/Chorizite/WorldBuilder.git
cd WorldBuilder

# Build the project
dotnet build

# Run the application
dotnet run --project WorldBuilder
```

## Contributing

Contributions are welcome! Please feel free to submit pull requests or open issues for bugs and feature requests.

## License

[Add license information here]

## Credits

Developed for the Asheron's Call community.
