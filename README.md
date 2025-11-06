# WorldBuilder

A modern cross-platform terrain and world editor for Asheron's Call, built with .NET 8 and Avalonia UI.

## Overview

WorldBuilder is a comprehensive world editing tool for Asheron's Call that provides terrain editing, static object placement, and world data management capabilities. Built on modern .NET technologies with a focus on performance and cross-platform support.

### Key Features

- **Terrain Editing**: Advanced terrain modification with brush-based tools, bucket fill, and vertex-level control
- **Road System**: Complete road drawing and editing system with line and point-based tools
- **Texture Painting**: Multi-layer texture painting with atlas management
- **Static Objects**: Place and manage static world objects
- **Document Management**: Project-based workflow with auto-save and batched updates
- **Undo/Redo System**: Full command history with snapshot support
- **3D Rendering**: Hardware-accelerated OpenGL rendering with frustum culling
- **Cross-Platform**: Runs on Windows, Linux, and web browsers via WebAssembly
- **Auto-Updates**: Integrated Sparkle updater for seamless updates

## Documentation

Comprehensive documentation is available:

- **[INDEX.md](docs/INDEX.md)** - Complete documentation index and navigation
- **[FEATURES.md](docs/FEATURES.md)** - Detailed feature documentation including:
  - History and Snapshot System
  - Layers and Groups Management
  - Terrain Editing Tools
  - Texture Painting System
  - Road Drawing System
- **[ARCHITECTURE.md](docs/ARCHITECTURE.md)** - Technical architecture documentation including:
  - System architecture and design patterns
  - Core system implementations
  - Rendering pipeline details
  - Performance optimizations
  - Extension points for developers
- **[API.md](docs/API.md)** - Complete API reference for:
  - Document System
  - Command/History System
  - Terrain System
  - Rendering System
  - Editor Tools
- **[COMPONENT_INDEX.md](docs/COMPONENT_INDEX.md)** - Quick reference index of all components

## Technology Stack

### Core Technologies
- **.NET 8.0**: Modern C# with nullable reference types and latest language features
- **Avalonia 11.3**: Cross-platform XAML-based UI framework
- **Entity Framework Core**: SQLite database for document storage
- **OpenGL/SDL**: Hardware-accelerated 3D rendering
- **MemoryPack**: High-performance binary serialization
- **CommunityToolkit.Mvvm**: MVVM pattern helpers

### Architecture Highlights
- **AOT Compilation**: Native AOT support for improved startup and performance
- **Command Pattern**: Undo/redo system for all editing operations
- **Document System**: Project-based workflow with batched persistence
- **MVVM Architecture**: Clean separation of concerns with data binding
- **Dependency Injection**: Microsoft.Extensions.DependencyInjection throughout

## Project Structure

```
WorldBuilder/
├── WorldBuilder/                      # Main desktop application
│   ├── Editors/                      # Editor implementations
│   │   └── Landscape/                # Terrain/landscape editor
│   │       ├── Commands/             # Undo/redo commands
│   │       ├── ViewModels/           # Editor UI logic
│   │       └── Views/                # Avalonia UI views
│   ├── Lib/                          # Core utilities
│   │   ├── History/                  # Command history system
│   │   ├── Settings/                 # Application settings
│   │   └── Converters/               # XAML value converters
│   ├── ViewModels/                   # Application view models
│   └── Views/                        # Application views
├── WorldBuilder.Shared/              # Shared business logic
│   ├── Documents/                    # Document management
│   ├── Lib/                          # Shared utilities
│   │   └── Resources/                # Resource management
│   ├── Models/                       # Data models
│   └── Migrations/                   # EF Core migrations
├── WorldBuilder.Desktop/             # Desktop app entry point
├── WorldBuilder.Browser/             # Browser/WASM entry point
└── Chorizite.OpenGLSDLBackend/       # OpenGL rendering backend
```

## Getting Started

### Prerequisites

- .NET 8.0 SDK or later
- Visual Studio 2022 or JetBrains Rider (recommended)
- Asheron's Call DAT files (cell.dat, portal.dat, highres.dat)

### Building

```bash
# Clone the repository
git clone https://github.com/chorizite/WorldBuilder.git
cd WorldBuilder

# Build the solution
dotnet build

# Run the desktop application
dotnet run --project WorldBuilder.Desktop
```

### First Launch

1. **Create a New Project**: On first launch, you'll be prompted to create or open a project
2. **Set DAT File Paths**: Configure paths to your Asheron's Call DAT files
3. **Project Database**: WorldBuilder creates a SQLite database to store your world data

## Core Systems

### Document Management System

The document management system provides a project-based workflow with automatic persistence:

- **BaseDocument**: Abstract base class for all document types
- **DocumentManager**: Manages document lifecycle and batched updates
- **Storage Service**: Abstracts document persistence (filesystem or database)
- **Auto-Save**: Batched updates every 2 seconds with configurable batch size

**Document Types:**
- `TerrainDocument`: Stores terrain height and texture data
- `LandblockDocument`: Manages landblock-specific data and objects

**Key Features:**
- Concurrent document access with thread-safe operations
- Batched updates reduce I/O overhead
- In-memory caching for active documents
- Projection-based serialization for efficient storage

### Command/History System

Full undo/redo support for all editing operations:

- **ICommand Interface**: Standard command pattern
- **CommandHistory**: Manages undo/redo stacks
- **CompositeCommand**: Group multiple commands
- **Snapshot Support**: Save/restore complete editor states

**Built-in Commands:**
- `PaintCommand`: Texture painting operations
- `TerrainVertexChangeCommand`: Height map modifications
- `RoadLineCommand`: Road segment creation
- `FillCommand`: Bucket fill operations

### Terrain System

Advanced terrain editing with chunk-based rendering:

**Components:**
- **TerrainSystem**: Main coordinator for terrain operations
- **TerrainEditingContext**: Provides editing APIs to tools
- **TerrainDataManager**: Manages height and texture data
- **TerrainGeometryGenerator**: Generates render meshes
- **TerrainGPUResourceManager**: Manages GPU buffers and textures
- **ChunkRenderData**: Per-chunk GPU resources

**Features:**
- LOD system with frustum culling
- Multi-threaded chunk generation
- Texture atlas management
- Real-time normal recalculation
- Optimized raycasting for editor picking

### Landscape Editor Tools

#### Texture Painting Tool
- **Brush Sub-Tool**: Paint textures with configurable brush size and strength
- **Bucket Fill Sub-Tool**: Fill regions with selected texture
- Supports undo/redo for all operations
- Real-time preview of changes

#### Road Drawing Tool
- **Line Sub-Tool**: Draw roads with straight line segments
- **Point Sub-Tool**: Place individual road points
- **Remove Sub-Tool**: Delete road segments
- Automatic texture application
- Configurable road width

### Rendering System

OpenGL-based 3D rendering with modern techniques:

**Features:**
- Deferred rendering pipeline
- Frustum culling for efficient rendering
- Instanced rendering for static objects
- Shader-based material system
- Camera system (perspective and orthographic)

**Render Components:**
- `OpenGLRenderer`: Main rendering coordinator
- `DrawList2`: Command buffer for draw calls
- `ManagedGLTexture`: Texture resource management
- `ManagedGLVertexBuffer`: Vertex buffer management
- `FontRenderer`: Text rendering system

## Configuration

### Application Settings

Settings are stored in `appsettings.json`:

```json
{
  "DatFilePaths": {
    "CellDat": "path/to/cell.dat",
    "PortalDat": "path/to/portal.dat",
    "HighResDat": "path/to/highres.dat"
  },
  "LandscapeEditor": {
    "BrushSize": 5.0,
    "BrushStrength": 0.5,
    "ShowGrid": true,
    "GridSize": 1.0
  }
}
```

### Project Settings

Each project maintains its own settings:
- Terrain resolution
- Texture atlas configuration
- Editor preferences
- Recent file history

## Development

### Key Design Patterns

- **MVVM**: Clean separation between UI and business logic
- **Command Pattern**: All editable operations are commands
- **Repository Pattern**: DocumentStorageService abstracts persistence
- **Factory Pattern**: View and editor creation
- **Observer Pattern**: Document change notifications
- **Composite Pattern**: Hierarchical service providers

### Adding New Tools

1. Create a ViewModel inheriting from `LandscapeToolViewModelBase`
2. Implement the tool logic in the ViewModel
3. Create commands for undo/redo support
4. Register in TerrainSystem's service collection
5. Create corresponding View (optional)

### Adding New Document Types

1. Inherit from `BaseDocument`
2. Implement `LoadFromProjection` and `SaveToProjection`
3. Override `InitAsync` for initialization logic
4. Register with DocumentManager

### Performance Considerations

- **Chunk-Based Rendering**: Only visible chunks are rendered
- **Batched Updates**: Document changes are batched every 2 seconds
- **GPU Resource Management**: Textures and buffers are cached
- **Async Operations**: Heavy operations use async/await
- **Memory Pooling**: Reuse allocated buffers where possible

## Testing

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test /p:CollectCoverage=true
```

## Deployment

### Desktop Application

```bash
# Publish for Windows (with installer)
dotnet publish WorldBuilder.Desktop -c Release -r win-x64 --self-contained

# Create installer (requires NSIS)
makensis Installer.nsi
```

### Browser Version

```bash
# Publish for WebAssembly
dotnet publish WorldBuilder.Browser -c Release
```

## Contributing

Contributions are welcome! Please follow these guidelines:

1. **Code Style**: Follow C# coding conventions
2. **Comments**: Document public APIs with XML comments
3. **Testing**: Add tests for new features
4. **Commits**: Use clear, descriptive commit messages
5. **Pull Requests**: Reference related issues

## Versioning

This project uses [GitVersion](https://gitversion.net/) for semantic versioning:
- Version is automatically calculated from git history
- Tags determine release versions
- Commits to master increment patch version

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Acknowledgments

- **Asheron's Call Community**: For keeping the game alive
- **Chorizite Project**: Core DAT file reading/writing library
- **Avalonia Team**: Excellent cross-platform UI framework

## Support

- **Issues**: [GitHub Issues](https://github.com/chorizite/WorldBuilder/issues)
- **Discord**: Join the Chorizite Discord server
- **Documentation**: See the [Wiki](https://github.com/chorizite/WorldBuilder/wiki)

## Roadmap

### Current Version (v1.0)
- ✅ Terrain editing
- ✅ Texture painting
- ✅ Road drawing
- ✅ Undo/redo system
- ✅ Project management

### Planned Features
- 🔜 Static object placement editor
- 🔜 Encounter/spawn editor
- 🔜 Dungeon editor
- 🔜 Lighting and environment editor
- 🔜 Multi-user collaboration
- 🔜 Plugin system

---

**Built with ❤️ for the Asheron's Call community**
