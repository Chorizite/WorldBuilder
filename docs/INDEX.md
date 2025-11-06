# WorldBuilder Documentation Index

Complete documentation index for the WorldBuilder project.

## Quick Start

- **[README.md](../README.md)** - Start here for project overview and getting started
- **[FEATURES.md](FEATURES.md)** - Comprehensive feature documentation
- **[ARCHITECTURE.md](ARCHITECTURE.md)** - Technical architecture details
- **[API.md](API.md)** - Complete API reference
- **[COMPONENT_INDEX.md](COMPONENT_INDEX.md)** - Quick reference to all components

## Documentation Overview

### User Documentation

#### Getting Started
- [Project Overview](../README.md#overview)
- [Technology Stack](../README.md#technology-stack)
- [Installation & Setup](../README.md#getting-started)
- [First Launch Guide](../README.md#first-launch)

#### Features
- [History and Snapshot System](FEATURES.md#history-and-snapshot-system)
- [Layers and Groups Management](FEATURES.md#layers-and-groups-management)
- [Terrain Editing](FEATURES.md#terrain-editing)
- [Texture Painting](FEATURES.md#texture-painting)
- [Road Drawing System](FEATURES.md#road-drawing-system)
- [Static Object Management](FEATURES.md#static-object-management)
- [Project Management](FEATURES.md#project-management)
- [Keyboard Shortcuts](FEATURES.md#keyboard-shortcuts)

### Developer Documentation

#### Architecture
- [System Overview](ARCHITECTURE.md#system-overview)
- [Architecture Patterns](ARCHITECTURE.md#architecture-patterns)
  - [MVVM Pattern](ARCHITECTURE.md#mvvm-model-view-viewmodel)
  - [Dependency Injection](ARCHITECTURE.md#dependency-injection)
  - [Command Pattern](ARCHITECTURE.md#command-pattern)
  - [Repository Pattern](ARCHITECTURE.md#repository-pattern)
  - [Observer Pattern](ARCHITECTURE.md#observer-pattern)

#### Core Systems
- [Document Management System](ARCHITECTURE.md#document-management-system)
- [Command/History System](ARCHITECTURE.md#commandhistory-system)
- [Terrain System](ARCHITECTURE.md#terrain-system)
- [Rendering Pipeline](ARCHITECTURE.md#rendering-pipeline)
- [Data Flow](ARCHITECTURE.md#data-flow)
- [Performance Optimizations](ARCHITECTURE.md#performance-optimizations)

#### API Reference
- [Document System API](API.md#document-system)
- [Command System API](API.md#command-system)
- [Terrain System API](API.md#terrain-system)
- [Rendering System API](API.md#rendering-system)
- [Editor Tools API](API.md#editor-tools)
- [Utilities API](API.md#utilities)

#### Component Index
- [Component Index by Type](COMPONENT_INDEX.md)
- [Component Index by Feature](COMPONENT_INDEX.md#by-feature-area)
- [Component Index by Layer](COMPONENT_INDEX.md#by-layer)
- [Component Relationships](COMPONENT_INDEX.md#component-relationships)

### External Resources
- [GitHub Repository](https://github.com/Chorizite/WorldBuilder)
- [GitHub Wiki](https://github.com/Chorizite/WorldBuilder/wiki)
- [Issue Tracker](https://github.com/Chorizite/WorldBuilder/issues)

---

## Documentation by Topic

### Document System

The document management system provides project-based workflow with automatic persistence.

**User Documentation:**
- [Project Management](FEATURES.md#project-management)

**Developer Documentation:**
- [Document System Architecture](ARCHITECTURE.md#document-management-system)
- [Document API Reference](API.md#document-system)
- [Document Components](COMPONENT_INDEX.md#document-system-components)

**Key Classes:**
- [`BaseDocument`](API.md#basedocument) - Abstract base for documents
- [`DocumentManager`](API.md#documentmanager) - Document lifecycle manager
- [`TerrainDocument`](API.md#terraindocument) - Terrain data document
- [`LandblockDocument`](API.md#landblockdocument) - Landblock data document

---

### History and Snapshots

Full undo/redo system with persistent snapshots, modeled after Photoshop's History panel.

**User Documentation:**
- [History and Snapshot System](FEATURES.md#history-and-snapshot-system)
  - [History Management](FEATURES.md#history-management)
  - [Snapshot Management](FEATURES.md#snapshot-management)
  - [UI Components](FEATURES.md#ui-components)

**Developer Documentation:**
- [Command Pattern](ARCHITECTURE.md#command-pattern)
- [History System Architecture](ARCHITECTURE.md#commandhistory-system)
- [Command API Reference](API.md#command-system)
- [Command Components](COMPONENT_INDEX.md#commandhistory-system-components)

**Key Classes:**
- [`ICommand`](API.md#icommand) - Command interface
- [`CommandHistory`](API.md#commandhistory) - History manager
- [`CompositeCommand`](API.md#compositecommand) - Command grouping
- All [Command Implementations](COMPONENT_INDEX.md#commandhistory-system-components)

---

### Layers and Groups

Hierarchical organization system for map content.

**User Documentation:**
- [Layers and Groups Management](FEATURES.md#layers-and-groups-management)
  - [Layer System](FEATURES.md#layer-system)
  - [Group System](FEATURES.md#group-system)
  - [Visibility Controls](FEATURES.md#visibility-controls)

**Developer Documentation:**
- Coming soon (implementation in progress)

---

### Terrain Editing

Advanced terrain modification with brush-based tools and vertex-level control.

**User Documentation:**
- [Terrain Editing](FEATURES.md#terrain-editing)
  - [Brush Tool](FEATURES.md#brush-tool)
  - [Bucket Fill Tool](FEATURES.md#bucket-fill-tool)

**Developer Documentation:**
- [Terrain System Architecture](ARCHITECTURE.md#terrain-system)
- [Chunk-Based Architecture](ARCHITECTURE.md#chunk-based-architecture)
- [Terrain API Reference](API.md#terrain-system)
- [Terrain Components](COMPONENT_INDEX.md#terrain-system-components)

**Key Classes:**
- [`TerrainSystem`](API.md#terrainsystem) - Main terrain coordinator
- [`TerrainEditingContext`](API.md#terraineditingcontext) - Editing API provider
- [`TerrainDataManager`](API.md#terraindatamanager) - Data management
- [`TerrainGeometryGenerator`](API.md#terraingeometrygenerator) - Mesh generation
- [`TerrainGPUResourceManager`](COMPONENT_INDEX.md#terrain-system-components) - GPU resources

---

### Texture Painting

Multi-layer texture system with atlas management and blending.

**User Documentation:**
- [Texture Painting](FEATURES.md#texture-painting)
  - [Texture Atlas System](FEATURES.md#texture-atlas-system)
  - [Painting Tools](FEATURES.md#painting-tools)

**Developer Documentation:**
- [Texture System](ARCHITECTURE.md#terrain-system)
- [Texture API](API.md#terrain-system)

**Key Classes:**
- [`TexturePaintingToolViewModel`](API.md#texturepaintingtoolviewmodel) - Painting tool
- [`BrushSubToolViewModel`](COMPONENT_INDEX.md#editor-tool-components) - Brush sub-tool
- [`BucketFillSubToolViewModel`](COMPONENT_INDEX.md#editor-tool-components) - Fill sub-tool
- [`TextureAtlasManager`](COMPONENT_INDEX.md#terrain-system-components) - Atlas management

---

### Road Drawing

Complete road creation and editing system.

**User Documentation:**
- [Road Drawing System](FEATURES.md#road-drawing-system)
  - [Road Types](FEATURES.md#road-types)
  - [Drawing Tools](FEATURES.md#drawing-tools)
  - [Road Properties](FEATURES.md#road-properties)

**Developer Documentation:**
- Road system documentation (in progress)

**Key Classes:**
- [`RoadDrawingToolViewModel`](API.md#roaddrawingtoolviewmodel) - Main road tool
- [`RoadLineSubToolViewModel`](COMPONENT_INDEX.md#editor-tool-components) - Line drawing
- [`RoadPointSubToolViewModel`](COMPONENT_INDEX.md#editor-tool-components) - Point placement
- [`RoadRemoveSubToolViewModel`](COMPONENT_INDEX.md#editor-tool-components) - Road removal

---

### Rendering System

OpenGL-based 3D rendering with modern techniques.

**User Documentation:**
- [3D Rendering](../README.md#key-features)

**Developer Documentation:**
- [Rendering Pipeline](ARCHITECTURE.md#rendering-pipeline)
- [OpenGL Backend Architecture](ARCHITECTURE.md#opengl-backend-architecture)
- [Rendering API Reference](API.md#rendering-system)
- [Rendering Components](COMPONENT_INDEX.md#rendering-system-components)

**Key Classes:**
- [`OpenGLRenderer`](API.md#openglrenderer) - Main renderer
- [`DrawList2`](API.md#drawlist2) - Draw command buffer
- [`GLSLShader`](API.md#glslshader) - Shader programs
- [`ManagedGLTexture`](API.md#managedgltexture) - Texture management
- [`ManagedGLVertexBuffer`](API.md#managedglvertexbuffer) - Vertex buffers

---

### Editor Tools

Extensible tool system for editing operations.

**User Documentation:**
- [Terrain Editing Tools](FEATURES.md#terrain-editing)
- [Texture Painting Tools](FEATURES.md#texture-painting)
- [Road Drawing Tools](FEATURES.md#road-drawing-system)

**Developer Documentation:**
- [Adding New Tools](ARCHITECTURE.md#adding-new-tools)
- [Editor Tools API](API.md#editor-tools)
- [Editor Tool Components](COMPONENT_INDEX.md#editor-tool-components)

**Key Classes:**
- [`LandscapeToolViewModelBase`](API.md#landscapetoolviewmodelbase) - Tool base class
- [`TexturePaintingToolViewModel`](API.md#texturepaintingtoolviewmodel)
- [`RoadDrawingToolViewModel`](API.md#roaddrawingtoolviewmodel)

---

## Documentation by User Type

### For End Users

If you're using WorldBuilder to edit Asheron's Call worlds:

1. Start with the [README](../README.md) to understand what WorldBuilder is
2. Follow [Getting Started](../README.md#getting-started) to install and configure
3. Learn the [Features](FEATURES.md) to understand what you can do
4. Reference [Keyboard Shortcuts](FEATURES.md#keyboard-shortcuts) while working
5. Check [Future Features](FEATURES.md#future-features) to see what's coming

### For Contributors

If you're contributing code to WorldBuilder:

1. Read [README](../README.md) for project overview
2. Study [ARCHITECTURE](ARCHITECTURE.md) to understand design patterns
3. Review [API Reference](API.md) for implementation details
4. Use [COMPONENT_INDEX](COMPONENT_INDEX.md) to navigate the codebase
5. Check [Extension Points](ARCHITECTURE.md#extension-points) for adding features
6. See [Testing Strategy](ARCHITECTURE.md#testing-strategy) for test guidelines

### For Plugin Developers

If you're extending WorldBuilder with plugins (future feature):

1. Read [Plugin System](FEATURES.md#future-features) (planned)
2. Study [Extension Points](ARCHITECTURE.md#extension-points)
3. Review [Custom Tool Development](ARCHITECTURE.md#adding-new-tools)
4. Reference [API Documentation](API.md)

---

## Common Tasks

### User Tasks

| Task | Documentation |
|------|---------------|
| Create a new project | [First Launch](../README.md#first-launch) |
| Edit terrain height | [Terrain Editing](FEATURES.md#terrain-editing) |
| Paint textures | [Texture Painting](FEATURES.md#texture-painting) |
| Draw roads | [Road Drawing](FEATURES.md#road-drawing-system) |
| Undo/redo changes | [History System](FEATURES.md#history-management) |
| Create snapshots | [Snapshot Management](FEATURES.md#snapshot-management) |
| Organize layers | [Layers and Groups](FEATURES.md#layers-and-groups-management) |
| Export DAT files | [Export System](FEATURES.md#export-system) |

### Developer Tasks

| Task | Documentation |
|------|---------------|
| Understand architecture | [Architecture](ARCHITECTURE.md) |
| Add new document type | [Custom Document Types](ARCHITECTURE.md#custom-document-types) |
| Create new tool | [Custom Editor Tools](ARCHITECTURE.md#custom-editor-tools) |
| Implement undo/redo | [Custom Commands](ARCHITECTURE.md#custom-commands) |
| Add rendering feature | [Rendering API](API.md#rendering-system) |
| Modify terrain system | [Terrain API](API.md#terrain-system) |
| Extend storage | [Custom Storage Providers](ARCHITECTURE.md#custom-storage-providers) |
| Run tests | [Testing](../README.md#testing) |
| Build project | [Building](../README.md#building) |
| Deploy application | [Deployment](ARCHITECTURE.md#build-and-deployment) |

---

## Documentation Status

### Complete ✅
- [x] README - Project overview and getting started
- [x] FEATURES - Comprehensive feature documentation
- [x] ARCHITECTURE - Technical architecture details
- [x] API - Complete API reference
- [x] COMPONENT_INDEX - Quick reference index
- [x] INDEX - Documentation navigation (this file)

### In Progress 🚧
- [ ] CONTRIBUTING - Contribution guidelines
- [ ] CHANGELOG - Version history
- [ ] LICENSE - License information

### Planned 📋
- [ ] PLUGIN_GUIDE - Plugin development guide
- [ ] PERFORMANCE - Performance tuning guide
- [ ] TROUBLESHOOTING - Common issues and solutions
- [ ] VIDEO_TUTORIALS - Video tutorial series
- [ ] MIGRATION_GUIDE - Version migration guides

---

## Contributing to Documentation

Documentation contributions are welcome! To contribute:

1. **Identify the Gap**: Is information missing or unclear?
2. **Choose the Right File**:
   - User-facing features → FEATURES.md
   - Technical architecture → ARCHITECTURE.md
   - API details → API.md
   - Component reference → COMPONENT_INDEX.md
3. **Follow the Style**: Match existing documentation style
4. **Update This Index**: Add links to your new content here
5. **Submit PR**: Create a pull request with your changes

### Documentation Standards

- Use clear, concise language
- Include code examples where appropriate
- Add diagrams for complex concepts
- Link related documentation
- Keep API docs in sync with code
- Update version information

---

## Quick Reference

### File Structure

```
WorldBuilder/
├── README.md              # Project overview and getting started
├── FEATURES.md            # Feature documentation
├── ARCHITECTURE.md        # Technical architecture
├── API.md                 # API reference
├── COMPONENT_INDEX.md     # Component quick reference
├── INDEX.md               # Documentation index (this file)
├── WorldBuilder/          # Main application
├── WorldBuilder.Shared/   # Shared business logic
├── WorldBuilder.Desktop/  # Desktop entry point
├── WorldBuilder.Browser/  # Browser entry point
└── Chorizite.OpenGLSDLBackend/  # Rendering backend
```

### Key Links

- **Repository**: https://github.com/Chorizite/WorldBuilder
- **Wiki**: https://github.com/Chorizite/WorldBuilder/wiki
- **Issues**: https://github.com/Chorizite/WorldBuilder/issues
- **Releases**: https://github.com/Chorizite/WorldBuilder/releases

---

**Last Updated**: November 6, 2025
**Documentation Version**: 1.0
**WorldBuilder Version**: 1.0.x
