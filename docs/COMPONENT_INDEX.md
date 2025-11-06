# WorldBuilder Component Index

Quick reference index for all major components in WorldBuilder.

## Document System Components

| Component | Location | Description |
|-----------|----------|-------------|
| `BaseDocument` | WorldBuilder.Shared/Documents/ | Abstract base class for all documents |
| `DocumentManager` | WorldBuilder.Shared/Documents/ | Manages document lifecycle and batched persistence |
| `DocumentStorageService` | WorldBuilder.Shared/Documents/ | EF Core-based document storage |
| `FileStorageService` | WorldBuilder.Shared/Documents/ | File-based document storage |
| `IDocumentStorageService` | WorldBuilder.Shared/Documents/ | Document storage abstraction |
| `TerrainDocument` | WorldBuilder.Shared/Documents/ | Stores terrain height/texture data |
| `LandblockDocument` | WorldBuilder.Shared/Documents/ | Stores landblock objects and data |
| `BaseDocumentEvent` | WorldBuilder.Shared/Documents/ | Document update event args |

## Command/History System Components

| Component | Location | Description |
|-----------|----------|-------------|
| `ICommand` | WorldBuilder/Lib/History/ | Command interface for undo/redo |
| `CommandHistory` | WorldBuilder/Lib/History/ | Manages undo/redo stacks |
| `CompositeCommand` | WorldBuilder/Lib/History/ | Groups multiple commands |
| `HistoryEntry` | WorldBuilder/Lib/History/ | History list item representation |
| `PaintCommand` | WorldBuilder/Editors/Landscape/Commands/ | Texture painting command |
| `TerrainVertexChangeCommand` | WorldBuilder/Editors/Landscape/Commands/ | Terrain height change command |
| `RoadLineCommand` | WorldBuilder/Editors/Landscape/Commands/ | Road segment creation command |
| `RoadChangeCommand` | WorldBuilder/Editors/Landscape/Commands/ | Road modification command |
| `FillCommand` | WorldBuilder/Editors/Landscape/Commands/ | Bucket fill command |

## Terrain System Components

| Component | Location | Description |
|-----------|----------|-------------|
| `TerrainSystem` | WorldBuilder/Editors/Landscape/ | Main terrain coordinator |
| `TerrainEditingContext` | WorldBuilder/Editors/Landscape/ | Provides editing APIs to tools |
| `TerrainDataManager` | WorldBuilder/Editors/Landscape/ | Manages height/texture data |
| `TerrainGeometryGenerator` | WorldBuilder/Editors/Landscape/ | Generates render meshes |
| `TerrainGPUResourceManager` | WorldBuilder/Editors/Landscape/ | Manages GPU buffers/textures |
| `TerrainRaycast` | WorldBuilder/Editors/Landscape/ | Raycasting against terrain |
| `TerrainChunk` | WorldBuilder/Editors/Landscape/ | Individual terrain chunk |
| `ChunkRenderData` | WorldBuilder/Editors/Landscape/ | Per-chunk render data |
| `ChunkMetrics` | WorldBuilder/Editors/Landscape/ | Chunk statistics |
| `GameScene` | WorldBuilder/Editors/Landscape/ | 3D scene coordinator |
| `LandSurfaceManager` | WorldBuilder/Editors/Landscape/ | Manages terrain surfaces |
| `StaticObjectManager` | WorldBuilder/Editors/Landscape/ | Manages static world objects |
| `TextureAtlasManager` | WorldBuilder/Editors/Landscape/ | Texture atlas management |
| `VertexPositionNormalTexture` | WorldBuilder/Editors/Landscape/ | Vertex structure |

## Rendering System Components

| Component | Location | Description |
|-----------|----------|-------------|
| `OpenGLRenderer` | Chorizite.OpenGLSDLBackend/ | Main OpenGL rendering coordinator |
| `OpenGLGraphicsDevice` | Chorizite.OpenGLSDLBackend/ | OpenGL device abstraction |
| `DrawList2` | Chorizite.OpenGLSDLBackend/ | Command buffer for draw calls |
| `GLSLShader` | Chorizite.OpenGLSDLBackend/ | GLSL shader program |
| `ManagedGLTexture` | Chorizite.OpenGLSDLBackend/ | Managed OpenGL texture |
| `ManagedGLTextureArray` | Chorizite.OpenGLSDLBackend/ | Managed texture array |
| `ManagedGLVertexBuffer` | Chorizite.OpenGLSDLBackend/ | Managed vertex buffer |
| `ManagedGLIndexBuffer` | Chorizite.OpenGLSDLBackend/ | Managed index buffer |
| `ManagedGLVertexArray` | Chorizite.OpenGLSDLBackend/ | Managed VAO |
| `ManagedGLFrameBuffer` | Chorizite.OpenGLSDLBackend/ | Managed FBO |
| `FontRenderer` | Chorizite.OpenGLSDLBackend/ | Text rendering |
| `AudioPlaybackEngine` | Chorizite.OpenGLSDLBackend/ | Audio playback |
| `GLHelpers` | Chorizite.OpenGLSDLBackend/ | OpenGL utility functions |

## Editor Tool Components

| Component | Location | Description |
|-----------|----------|-------------|
| `EditorBase` | WorldBuilder/Editors/ | Base class for all editors |
| `IEditor` | WorldBuilder/Editors/ | Editor interface |
| `LandscapeEditorViewModel` | WorldBuilder/Editors/Landscape/ViewModels/ | Main landscape editor VM |
| `LandscapeToolViewModelBase` | WorldBuilder/Editors/Landscape/ViewModels/ | Base class for tools |
| `LandscapeSubToolViewModelBase` | WorldBuilder/Editors/Landscape/ViewModels/ | Base class for sub-tools |
| `TexturePaintingToolViewModel` | WorldBuilder/Editors/Landscape/ViewModels/ | Texture painting tool |
| `BrushSubToolViewModel` | WorldBuilder/Editors/Landscape/ViewModels/ | Brush painting sub-tool |
| `BucketFillSubToolViewModel` | WorldBuilder/Editors/Landscape/ViewModels/ | Bucket fill sub-tool |
| `RoadDrawingToolViewModel` | WorldBuilder/Editors/Landscape/ViewModels/ | Road drawing tool |
| `RoadLineSubToolViewModel` | WorldBuilder/Editors/Landscape/ViewModels/ | Road line sub-tool |
| `RoadPointSubToolViewModel` | WorldBuilder/Editors/Landscape/ViewModels/ | Road point sub-tool |
| `RoadRemoveSubToolViewModel` | WorldBuilder/Editors/Landscape/ViewModels/ | Road removal sub-tool |
| `ObjectDebugViewModel` | WorldBuilder/Editors/Landscape/ViewModels/ | Object debug panel |

## UI Components (ViewModels)

| Component | Location | Description |
|-----------|----------|-------------|
| `MainViewModel` | WorldBuilder/ViewModels/ | Main window view model |
| `ViewModelBase` | WorldBuilder/ViewModels/ | Base class for all VMs |
| `SplashPageViewModel` | WorldBuilder/ViewModels/ | Splash page VM |
| `SplashPageViewModelBase` | WorldBuilder/ViewModels/ | Splash page base VM |
| `CreateProjectViewModel` | WorldBuilder/ViewModels/ | Project creation VM |
| `ProjectSelectionViewModel` | WorldBuilder/ViewModels/ | Project selection VM |
| `ExportDatsWindowViewModel` | WorldBuilder/ViewModels/ | DAT export VM |
| `HistorySnapshotPanelViewModel` | WorldBuilder/ViewModels/ | History/snapshot panel VM |
| `HistoryListItem` | WorldBuilder/ViewModels/ | History list item |

## UI Components (Views)

| Component | Location | Description |
|-----------|----------|-------------|
| `MainWindow` | WorldBuilder/Views/ | Main application window |
| `MainView` | WorldBuilder/Views/ | Main view (for browser) |
| `SplashPageWindow` | WorldBuilder/Views/ | Splash screen window |
| `ProjectSelectionView` | WorldBuilder/Views/ | Project selection view |
| `CreateProjectView` | WorldBuilder/Views/ | Project creation view |
| `SettingsWindow` | WorldBuilder/Views/ | Settings window |
| `ExportDatsWindow` | WorldBuilder/Views/ | DAT export window |
| `HistorySnapshotPanelView` | WorldBuilder/Views/ | History panel view |
| `Base3DView` | WorldBuilder/Views/ | Base class for 3D views |
| `LandscapeEditorView` | WorldBuilder/Editors/Landscape/Views/ | Landscape editor view |
| `BrushSubToolView` | WorldBuilder/Editors/Landscape/Views/ | Brush sub-tool view |
| `BucketFillSubToolView` | WorldBuilder/Editors/Landscape/Views/ | Bucket fill sub-tool view |
| `ObjectDebugView` | WorldBuilder/Editors/Landscape/Views/ | Object debug view |

## Utility Components

| Component | Location | Description |
|-----------|----------|-------------|
| `Camera` | WorldBuilder/Lib/ | Camera base class |
| `PerspectiveCamera` | WorldBuilder/Services/ | Perspective camera |
| `OrthographicTopDownCamera` | WorldBuilder/Services/ | Orthographic camera |
| `MouseState` | WorldBuilder/Lib/ | Mouse input state |
| `AvaloniaInputState` | WorldBuilder/Lib/ | Avalonia input adapter |
| `ViewLocator` | WorldBuilder/Lib/ | MVVM view locator |
| `ProjectManager` | WorldBuilder/Lib/ | Manages project lifecycle |
| `SceneryHelpers` | WorldBuilder/Lib/ | Scenery utility functions |
| `CustomLogging` | WorldBuilder/Lib/ | Custom logging configuration |
| `CompositeServiceProvider` | WorldBuilder/Lib/ | Hierarchical service provider |
| `WorldBuilderBackend` | WorldBuilder/Lib/ | Platform backend abstraction |

## Settings Components

| Component | Location | Description |
|-----------|----------|-------------|
| `WorldBuilderSettings` | WorldBuilder/Lib/Settings/ | Main application settings |
| `AppSettings` | WorldBuilder/Lib/Settings/ | App-level settings |
| `LandscapeEditorSettings` | WorldBuilder/Lib/Settings/ | Landscape editor settings |
| `SettingsAttributes` | WorldBuilder/Lib/Settings/ | Settings metadata attributes |
| `SettingsMetaData` | WorldBuilder/Lib/Settings/ | Settings metadata |
| `SettingsCloner` | WorldBuilder/Lib/Settings/ | Settings deep copy utility |
| `SettingsUIGenerator` | WorldBuilder/Lib/Settings/ | Auto-generates settings UI |
| `SettingsUIHandlers` | WorldBuilder/Lib/Settings/ | Settings UI event handlers |

## Converter Components

| Component | Location | Description |
|-----------|----------|-------------|
| `BoolConverters` | WorldBuilder/Lib/Converters/ | Boolean value converters |
| `BoolToStringConverter` | WorldBuilder/Lib/Converters/ | Bool to string converter |
| `IdToStringConverter` | WorldBuilder/Lib/Converters/ | ID to string converter |
| `IsNotZeroConverter` | WorldBuilder/Lib/Converters/ | Zero check converter |
| `KeyEventArgsConverter` | WorldBuilder/Lib/Converters/ | Key event converter |
| `LogLevelToStringConverter` | WorldBuilder/Lib/Converters/ | Log level converter |
| `ObjectConverters` | WorldBuilder/Lib/Converters/ | Generic object converters |
| `Vector3ToColorConverter` | WorldBuilder/Lib/Converters/ | Vector3 to color converter |

## Behavior Components

| Component | Location | Description |
|-----------|----------|-------------|
| `ListBoxItemsClassesBehavior` | WorldBuilder/Lib/Behaviors/ | ListBox styling behavior |

## Factory Components

| Component | Location | Description |
|-----------|----------|-------------|
| `SplashPageFactory` | WorldBuilder/Lib/Factories/ | Creates splash page VMs |

## Message Components

| Component | Location | Description |
|-----------|----------|-------------|
| `CreateProjectMessage` | WorldBuilder/Lib/Messages/ | Create project message |
| `OpenProjectMessage` | WorldBuilder/Lib/Messages/ | Open project message |
| `PageChangedMessage` | WorldBuilder/Lib/Messages/ | Page navigation message |
| `DocumentEventArgs` | WorldBuilder/Editors/ | Document event arguments |

## Data Model Components

| Component | Location | Description |
|-----------|----------|-------------|
| `DBDocument` | WorldBuilder.Shared/Models/ | Database document entity |
| `DBDocumentUpdate` | WorldBuilder.Shared/Models/ | Document update record |
| `DBSnapshot` | WorldBuilder.Shared/Models/ | Document snapshot entity |
| `DocumentStats` | WorldBuilder.Shared/Models/ | Document statistics |
| `Project` | WorldBuilder.Shared/Models/ | Project model |

## Resource Management Components

| Component | Location | Description |
|-----------|----------|-------------|
| `ResourceManager` | WorldBuilder.Shared/Lib/Resources/ | Manages DAT resources |
| `IResource` | WorldBuilder.Shared/Lib/Resources/ | Resource interface |
| `IDatResource` | WorldBuilder.Shared/Lib/Resources/ | DAT resource interface |
| `IModelResource` | WorldBuilder.Shared/Lib/Resources/ | 3D model resource interface |
| `DatFile` | WorldBuilder.Shared/Lib/Resources/ | DAT file wrapper |

## Database Components

| Component | Location | Description |
|-----------|----------|-------------|
| `DocumentDbContext` | WorldBuilder.Shared/Lib/ | EF Core DbContext |
| `AddDocumentsAndUpdatesTables` | WorldBuilder.Shared/Migrations/ | Initial migration |
| `AddSnapshotsTable` | WorldBuilder.Shared/Migrations/ | Snapshots migration |
| `DocumentDbContextModelSnapshot` | WorldBuilder.Shared/Migrations/ | EF Core snapshot |

## Extension Components

| Component | Location | Description |
|-----------|----------|-------------|
| `ServiceCollectionExtensions` | WorldBuilder/Lib/Extensions/ | DI registration helpers |
| `ServiceCollectionExtensions` | WorldBuilder.Shared/Lib/Extensions/ | Shared DI helpers |
| `ColorARGBExtensions` | WorldBuilder.Shared/Lib/Extensions/ | Color utility methods |
| `BufferUsageExtensions` | Chorizite.OpenGLSDLBackend/Extensions/ | Buffer usage helpers |
| `TextureFormatExtensions` | Chorizite.OpenGLSDLBackend/Extensions/ | Texture format helpers |

## DAT File Components

| Component | Location | Description |
|-----------|----------|-------------|
| `IDatReaderWriter` | WorldBuilder.Shared/Lib/ | DAT I/O interface |
| `DefaultDatReaderWriter` | WorldBuilder.Shared/Lib/ | Default DAT implementation |

## Shader Resources

| Shader | Location | Description |
|--------|----------|-------------|
| `vertex.glsl` | WorldBuilder/Shaders/ | Main vertex shader |
| `fragment.glsl` | WorldBuilder/Shaders/ | Main fragment shader |
| `Sphere.vert` | WorldBuilder/Shaders/ | Sphere vertex shader |
| `Sphere.frag` | WorldBuilder/Shaders/ | Sphere fragment shader |

## Helper Utilities

| Component | Location | Description |
|-----------|----------|-------------|
| `DxUtil` | Chorizite.OpenGLSDLBackend/Lib/ | DirectX utility functions |
| `EmbeddedResourceReader` | Chorizite.OpenGLSDLBackend/Lib/ | Reads embedded resources |
| `TextureHelpers` | Chorizite.OpenGLSDLBackend/Lib/ | Texture utility functions |
| `JsonSourceGenerationContext` | WorldBuilder/Lib/ | JSON serialization context |
| `AppCastFilter` | WorldBuilder/Lib/ | Sparkle appcast filter |

## Quick Navigation

### By Feature Area

**Terrain Editing**
- TerrainSystem
- TerrainEditingContext
- TerrainDataManager
- TerrainGeometryGenerator
- LandscapeEditorViewModel

**Document Management**
- DocumentManager
- BaseDocument
- TerrainDocument
- LandblockDocument
- DocumentStorageService

**Undo/Redo**
- CommandHistory
- ICommand
- CompositeCommand
- All *Command classes

**Rendering**
- OpenGLRenderer
- DrawList2
- ManagedGL* classes
- GLSLShader

**UI/Views**
- MainViewModel
- MainWindow
- LandscapeEditorView
- All *ViewModel classes

### By Layer

**Presentation (UI)**
- Views/, ViewModels/
- Converters/, Behaviors/

**Application Logic**
- Editors/, Lib/
- Settings/, History/

**Business Logic**
- WorldBuilder.Shared/Documents/
- WorldBuilder.Shared/Lib/

**Data Access**
- DocumentStorageService
- DocumentDbContext
- Migrations/

**Infrastructure**
- Chorizite.OpenGLSDLBackend/
- Extensions/

---

## Component Relationships

### High-Level Flow

```
UI (Views)
    ↓ binds to
ViewModels
    ↓ uses
Editors (TerrainSystem)
    ↓ operates on
Documents (TerrainDocument)
    ↓ persisted by
DocumentManager
    ↓ saves to
DocumentStorageService (EF Core)
    ↓ writes to
SQLite Database
```

### Command Flow

```
User Action
    ↓
ViewModel creates Command
    ↓
CommandHistory.ExecuteCommand()
    ↓
Command modifies Document
    ↓
Document notifies Update event
    ↓
DocumentManager queues update
    ↓
Batch processor saves to DB
```

### Render Flow

```
TerrainSystem.Update()
    ↓
TerrainGeometryGenerator
    ↓
TerrainGPUResourceManager
    ↓
OpenGLRenderer
    ↓
DrawList2
    ↓
OpenGL calls
```

---

## See Also

- [README.md](../README.md) - Project overview
- [FEATURES.md](FEATURES.md) - Feature documentation
- [ARCHITECTURE.md](ARCHITECTURE.md) - Architecture details
- [API.md](API.md) - API reference
