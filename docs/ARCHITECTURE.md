# WorldBuilder Architecture

Technical architecture documentation for developers working on WorldBuilder.

## Table of Contents

- [System Overview](#system-overview)
- [Architecture Patterns](#architecture-patterns)
- [Core Systems](#core-systems)
- [Rendering Pipeline](#rendering-pipeline)
- [Data Flow](#data-flow)
- [Performance Optimizations](#performance-optimizations)
- [Extension Points](#extension-points)

---

## System Overview

WorldBuilder is built on a modern .NET 8 architecture using Avalonia UI for cross-platform desktop support and OpenGL for 3D rendering. The application follows MVVM patterns with dependency injection throughout.

### Technology Stack

```
┌─────────────────────────────────────────┐
│         Avalonia UI (11.3.7)            │ Presentation
├─────────────────────────────────────────┤
│    ViewModels (CommunityToolkit.Mvvm)   │ Presentation Logic
├─────────────────────────────────────────┤
│         Business Logic Layer            │
│  - Document Management                  │
│  - Command/History System               │
│  - Terrain System                       │
│  - Editor Tools                         │
├─────────────────────────────────────────┤
│          Data Access Layer              │
│  - Entity Framework Core                │
│  - Document Storage Service             │
│  - DAT File I/O                         │
├─────────────────────────────────────────┤
│        Rendering & Platform             │
│  - OpenGL/SDL Backend                   │
│  - .NET 8 Runtime                       │
└─────────────────────────────────────────┘
```

### Project Dependencies

```
WorldBuilder.Desktop
    ├── WorldBuilder (main app)
    │   ├── Chorizite.OpenGLSDLBackend
    │   │   ├── OpenTK (OpenGL bindings)
    │   │   └── SDL2-CS (SDL bindings)
    │   └── WorldBuilder.Shared
    │       └── Chorizite.DatReaderWriter
    └── Avalonia.Desktop

WorldBuilder.Browser
    └── WorldBuilder (main app)
        └── Avalonia.Browser
```

---

## Architecture Patterns

### MVVM (Model-View-ViewModel)

WorldBuilder strictly follows the MVVM pattern:

**Views (Avalonia XAML)**
- Pure presentation layer
- Data binding to ViewModels
- No business logic
- Event routing to commands

**ViewModels**
- Presentation logic
- Commands for user actions
- Observable properties
- View-agnostic

**Models**
- Business logic
- Data structures
- Domain operations
- Persistence

**Example:**
```csharp
// View (XAML)
<Button Command="{Binding PaintCommand}" Content="Paint" />

// ViewModel
[RelayCommand]
private void Paint() {
    var command = new PaintCommand(/* params */);
    _history.ExecuteCommand(command);
}

// Model/Domain
public class PaintCommand : ICommand {
    public void Execute() { /* apply paint */ }
    public void Undo() { /* revert paint */ }
}
```

### Dependency Injection

Microsoft.Extensions.DependencyInjection is used throughout:

```csharp
// Service Registration (App.axaml.cs)
services.AddSingleton<ProjectManager>();
services.AddSingleton<DocumentManager>();
services.AddTransient<LandscapeEditorViewModel>();

// Service Resolution
var editor = serviceProvider.GetRequiredService<LandscapeEditorViewModel>();
```

**Composite Service Providers**
The application uses a hierarchical service provider pattern:
- Global services (application lifetime)
- Project services (per-project lifetime)
- Editor services (per-editor lifetime)

```csharp
public class CompositeServiceProvider : IServiceProvider {
    private readonly IServiceProvider _primary;
    private readonly IServiceProvider? _fallback;

    public object? GetService(Type serviceType) {
        return _primary.GetService(serviceType)
            ?? _fallback?.GetService(serviceType);
    }
}
```

### Command Pattern

All editing operations implement `ICommand` for undo/redo support:

```csharp
public interface ICommand {
    void Execute();
    void Undo();
    string Description { get; }
}

public class CommandHistory {
    private readonly Stack<ICommand> _undoStack = new();
    private readonly Stack<ICommand> _redoStack = new();

    public void ExecuteCommand(ICommand command) {
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear(); // Clear redo on new action
    }

    public void Undo() {
        if (_undoStack.TryPop(out var command)) {
            command.Undo();
            _redoStack.Push(command);
        }
    }
}
```

**Composite Commands**
Multiple commands can be grouped:

```csharp
public class CompositeCommand : ICommand {
    private readonly List<ICommand> _commands = new();

    public void AddCommand(ICommand command) => _commands.Add(command);

    public void Execute() {
        foreach (var cmd in _commands) cmd.Execute();
    }

    public void Undo() {
        // Undo in reverse order
        for (int i = _commands.Count - 1; i >= 0; i--) {
            _commands[i].Undo();
        }
    }
}
```

### Repository Pattern

Document storage is abstracted via `IDocumentStorageService`:

```csharp
public interface IDocumentStorageService : IDisposable {
    Task<DBDocument?> GetDocumentAsync(string documentId);
    Task<DBDocument> CreateDocumentAsync(string id, string type, byte[] data);
    Task UpdateDocumentAsync(string id, byte[] data);
    Task DeleteDocumentAsync(string documentId);
}

// Implementations
public class FileStorageService : IDocumentStorageService { }
public class DocumentStorageService : IDocumentStorageService { // EF Core }
```

### Observer Pattern

Documents notify subscribers of changes:

```csharp
public abstract class BaseDocument {
    public event EventHandler<UpdateEventArgs>? Update;

    protected void NotifyUpdate() {
        Update?.Invoke(this, new UpdateEventArgs(this));
    }
}

// DocumentManager subscribes
document.Update += HandleDocumentUpdate;
```

---

## Core Systems

### Document Management System

#### Document Lifecycle

```
┌──────────────┐
│   Request    │ GetOrCreateDocumentAsync<T>()
│   Document   │
└──────┬───────┘
       │
       ▼
┌──────────────┐
│Check In-Memory│ _activeDocs.TryGetValue()
│    Cache      │
└──────┬───────┘
       │
       ▼ Cache Miss
┌──────────────┐
│ Load from DB │ DocumentStorageService.GetDocumentAsync()
│   or Create  │
└──────┬───────┘
       │
       ▼
┌──────────────┐
│ Deserialize  │ LoadFromProjection()
│   Document   │
└──────┬───────┘
       │
       ▼
┌──────────────┐
│  Initialize  │ InitAsync(dats, docManager)
│   Document   │
└──────┬───────┘
       │
       ▼
┌──────────────┐
│ Add to Cache │ _activeDocs.TryAdd()
│  & Subscribe │ document.Update += Handler
└──────┬───────┘
       │
       ▼
┌──────────────┐
│Return Document│
└──────────────┘
```

#### Batched Update System

Documents use a batched update system to reduce I/O overhead:

```csharp
// Update Flow
Document Modified
    ↓
NotifyUpdate() called
    ↓
Update queued in Channel (1000 capacity)
    ↓
Batch Processor (background thread)
    ↓
Wait 2 seconds or 50 updates
    ↓
Group by DocumentId, keep latest
    ↓
Parallel save (16 concurrent max)
    ↓
DocumentStorageService.UpdateDocumentAsync()
```

**Configuration:**
- Batch interval: 2 seconds
- Max batch size: 50 updates
- Concurrent saves: 16
- Queue capacity: 1000 updates

#### Document Serialization

Documents use `MemoryPack` for efficient binary serialization:

```csharp
[MemoryPackable]
public partial class TerrainDocumentProjection {
    public Dictionary<ulong, TerrainChunkData> Chunks { get; set; }
    public List<string> TextureIds { get; set; }
}

public abstract class BaseDocument {
    public abstract byte[] SaveToProjection();
    public abstract bool LoadFromProjection(byte[] data);
}

// Implementation
public class TerrainDocument : BaseDocument {
    public override byte[] SaveToProjection() {
        var projection = new TerrainDocumentProjection {
            Chunks = _chunks.ToDictionary(k => k.Key, v => v.Value.Data)
        };
        return MemoryPackSerializer.Serialize(projection);
    }
}
```

### Command/History System

#### Command Implementation

```csharp
// Example: Paint Command
public class PaintCommand : ICommand {
    private readonly ulong _chunkId;
    private readonly int _vertexIndex;
    private readonly byte _newTextureId;
    private readonly byte _oldTextureId;
    private readonly TerrainDocument _document;

    public string Description => $"Paint Texture {_newTextureId}";

    public void Execute() {
        _document.SetVertexTexture(_chunkId, _vertexIndex, _newTextureId);
    }

    public void Undo() {
        _document.SetVertexTexture(_chunkId, _vertexIndex, _oldTextureId);
    }
}
```

#### History Management

```csharp
public class CommandHistory {
    private readonly Stack<ICommand> _undoStack = new();
    private readonly Stack<ICommand> _redoStack = new();
    private const int MaxHistorySize = 50;

    public event EventHandler<TrimHistoryEventArgs>? TrimHistory;

    public void ExecuteCommand(ICommand command) {
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear();

        // Trim old history
        if (_undoStack.Count > MaxHistorySize) {
            var toRemove = _undoStack.Count - MaxHistorySize;
            TrimHistory?.Invoke(this, new TrimHistoryEventArgs(toRemove));

            // Keep only recent history
            _undoStack = new Stack<ICommand>(
                _undoStack.Take(MaxHistorySize).Reverse()
            );
        }
    }
}
```

#### Snapshot System

Snapshots store complete document state:

```csharp
public class DBSnapshot {
    public string Id { get; set; }
    public string DocumentId { get; set; }
    public string Name { get; set; }
    public byte[] Data { get; set; }  // Full document projection
    public DateTime Created { get; set; }
}

// Creating a snapshot
public async Task CreateSnapshot(string name) {
    var snapshot = new DBSnapshot {
        Id = Guid.NewGuid().ToString(),
        DocumentId = _document.Id,
        Name = name,
        Data = _document.SaveToProjection(),
        Created = DateTime.UtcNow
    };
    await _storageService.SaveSnapshotAsync(snapshot);
}

// Restoring a snapshot
public async Task RestoreSnapshot(DBSnapshot snapshot) {
    _document.LoadFromProjection(snapshot.Data);
    _history.Clear(); // Clear history on snapshot restore
}
```

### Terrain System

#### Chunk-Based Architecture

Terrain is divided into chunks for efficient rendering and editing:

```
World (Dereth)
    ├── Landblock (256m x 256m)
    │   ├── Chunk 0 (64m x 64m)
    │   ├── Chunk 1
    │   ├── Chunk 2
    │   └── Chunk 3
    └── ...
```

**Chunk Structure:**
- Size: 64m x 64m (configurable)
- Vertices: 17x17 grid (1m spacing)
- Triangles: 16x16 quads (2 triangles each)
- LOD Levels: 4 (1m, 2m, 4m, 8m)

#### Terrain Data Management

```csharp
public class TerrainDataManager {
    private Dictionary<ulong, TerrainChunkData> _chunks;

    public float GetHeight(int x, int z);
    public void SetHeight(int x, int z, float height);
    public byte GetTexture(int x, int z);
    public void SetTexture(int x, int z, byte textureId);
    public Vector3 GetNormal(int x, int z);
}

public class TerrainChunkData {
    public ushort[] Heights;     // 17x17 height values
    public byte[] Textures;      // 17x17 texture indices
    public Vector3[] Normals;    // Calculated from heights
}
```

#### Terrain Rendering Pipeline

```
Update Phase:
    Camera.GetFrustum()
        ↓
    Frustum Culling (check all chunks)
        ↓
    Generate/Update Dirty Chunks
        ↓
    Update GPU Buffers (vertex/index)

Render Phase:
    Set Shader & Uniforms
        ↓
    Bind Texture Atlas
        ↓
    For each visible chunk:
        - Bind vertex buffer
        - Bind index buffer
        - DrawElements()
```

#### Chunk Generation

```csharp
public class TerrainGeometryGenerator {
    public ChunkMeshData GenerateMesh(TerrainChunkData data, int lod) {
        var vertices = new List<VertexPositionNormalTexture>();
        var indices = new List<uint>();

        int step = 1 << lod; // LOD multiplier (1, 2, 4, 8)

        for (int z = 0; z < 17; z += step) {
            for (int x = 0; x < 17; x += step) {
                var vertex = new VertexPositionNormalTexture {
                    Position = new Vector3(x, data.Heights[z * 17 + x], z),
                    Normal = data.Normals[z * 17 + x],
                    TexCoord = new Vector2(x / 16f, z / 16f)
                };
                vertices.Add(vertex);
            }
        }

        // Generate triangle indices (skipped for brevity)

        return new ChunkMeshData { Vertices = vertices, Indices = indices };
    }
}
```

---

## Rendering Pipeline

### OpenGL Backend Architecture

```
OpenGLRenderer
    ├── DrawList2 (command buffer)
    ├── ManagedGLTexture (texture cache)
    ├── ManagedGLVertexBuffer (VBO pool)
    ├── ManagedGLIndexBuffer (IBO pool)
    └── GLSLShader (shader management)
```

### Render Frame Flow

```
BeginFrame()
    ↓
Clear Color/Depth Buffers
    ↓
Update Camera (view/projection matrices)
    ↓
Frustum Culling
    ↓
Update Terrain Chunks (if dirty)
    ↓
Generate Draw Commands
    ↓
Sort Draw Commands (by shader, texture, depth)
    ↓
Execute Draw Commands
    ├── Bind Shader
    ├── Set Uniforms
    ├── Bind Textures
    ├── Bind Vertex/Index Buffers
    └── DrawElements()
    ↓
Render UI Overlay (ImGui-style)
    ↓
Swap Buffers
    ↓
EndFrame()
```

### Shader System

**Vertex Shader (terrain.vert):**
```glsl
#version 330 core

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;

uniform mat4 uViewProjection;
uniform mat4 uModel;

out vec3 vNormal;
out vec2 vTexCoord;
out vec3 vWorldPos;

void main() {
    vec4 worldPos = uModel * vec4(aPosition, 1.0);
    vWorldPos = worldPos.xyz;
    vNormal = mat3(uModel) * aNormal;
    vTexCoord = aTexCoord;
    gl_Position = uViewProjection * worldPos;
}
```

**Fragment Shader (terrain.frag):**
```glsl
#version 330 core

in vec3 vNormal;
in vec2 vTexCoord;
in vec3 vWorldPos;

uniform sampler2DArray uTextureAtlas;
uniform vec3 uLightDir;
uniform vec3 uCameraPos;

out vec4 FragColor;

void main() {
    // Sample texture from atlas
    vec4 texColor = texture(uTextureAtlas, vec3(vTexCoord, 0));

    // Simple directional lighting
    float ndotl = max(dot(normalize(vNormal), uLightDir), 0.0);
    vec3 diffuse = texColor.rgb * (0.3 + 0.7 * ndotl);

    // Fog
    float dist = length(vWorldPos - uCameraPos);
    float fog = exp(-dist * 0.01);
    diffuse = mix(vec3(0.5, 0.6, 0.7), diffuse, fog);

    FragColor = vec4(diffuse, 1.0);
}
```

### GPU Resource Management

```csharp
public class TerrainGPUResourceManager {
    private readonly Dictionary<ulong, ChunkGPUData> _chunkResources = new();

    public void UpdateChunk(ulong chunkId, ChunkMeshData meshData) {
        if (!_chunkResources.TryGetValue(chunkId, out var gpu)) {
            gpu = new ChunkGPUData {
                VertexBuffer = new ManagedGLVertexBuffer(),
                IndexBuffer = new ManagedGLIndexBuffer()
            };
            _chunkResources[chunkId] = gpu;
        }

        gpu.VertexBuffer.SetData(meshData.Vertices);
        gpu.IndexBuffer.SetData(meshData.Indices);
        gpu.IndexCount = meshData.Indices.Count;
    }

    public void RenderChunk(ulong chunkId) {
        if (_chunkResources.TryGetValue(chunkId, out var gpu)) {
            gpu.VertexBuffer.Bind();
            gpu.IndexBuffer.Bind();
            GL.DrawElements(PrimitiveType.Triangles, gpu.IndexCount,
                DrawElementsType.UnsignedInt, IntPtr.Zero);
        }
    }
}
```

---

## Data Flow

### Document Update Flow

```
User Action (e.g., paint brush)
    ↓
ViewModel.PaintCommand()
    ↓
Create PaintCommand
    ↓
CommandHistory.ExecuteCommand()
    ↓
PaintCommand.Execute()
    ↓
TerrainDocument.SetVertexTexture()
    ↓
Mark document dirty
    ↓
NotifyUpdate() → Update event
    ↓
DocumentManager.HandleDocumentUpdate()
    ↓
Queue update in Channel
    ↓
[Background Thread]
Batch Processor wakes up
    ↓
Collect pending updates (2 sec or 50 updates)
    ↓
Group by DocumentId, keep latest
    ↓
For each document:
    SaveToProjection()
        ↓
    DocumentStorageService.UpdateDocumentAsync()
        ↓
    EF Core SaveChangesAsync()
        ↓
    SQLite Write
```

### Rendering Update Flow

```
Document Modified (terrain height change)
    ↓
TerrainDocument.NotifyUpdate()
    ↓
TerrainSystem subscribed to Update event
    ↓
TerrainSystem.HandleTerrainUpdate()
    ↓
Mark affected chunks as dirty
    ↓
[Next Render Frame]
TerrainSystem.Update()
    ↓
For each dirty chunk:
    TerrainGeometryGenerator.GenerateMesh()
        ↓
    TerrainGPUResourceManager.UpdateChunk()
        ↓
    Upload to GPU
    ↓
Clear dirty flag
    ↓
Render updated chunk
```

---

## Performance Optimizations

### Chunk Streaming

Only visible chunks are loaded and rendered:

```csharp
public void Update(Vector3 cameraPos, Frustum frustum) {
    // Determine visible chunks
    var visibleChunks = _allChunks
        .Where(c => frustum.Intersects(c.BoundingBox))
        .ToList();

    // Load visible chunks
    foreach (var chunkId in visibleChunks) {
        if (!_loadedChunks.Contains(chunkId)) {
            LoadChunk(chunkId);
        }
    }

    // Unload distant chunks
    var toUnload = _loadedChunks
        .Where(id => !visibleChunks.Contains(id))
        .Where(id => Vector3.Distance(cameraPos, GetChunkCenter(id)) > UnloadDistance)
        .ToList();

    foreach (var chunkId in toUnload) {
        UnloadChunk(chunkId);
    }
}
```

### Batched Document Updates

As described earlier, document updates are batched to reduce I/O:

**Benefits:**
- Reduces write operations by ~95%
- Groups multiple edits into single transaction
- Prevents database lock contention
- Improves UI responsiveness

### GPU Resource Pooling

```csharp
public class ManagedGLVertexBuffer : IDisposable {
    private static readonly Stack<int> _bufferPool = new();
    private int _handle;

    public ManagedGLVertexBuffer() {
        _handle = _bufferPool.TryPop(out var pooled)
            ? pooled
            : GL.GenBuffer();
    }

    public void Dispose() {
        if (_handle != 0) {
            _bufferPool.Push(_handle);
            _handle = 0;
        }
    }
}
```

### LOD System

Terrain chunks use distance-based LOD:

```csharp
public int GetLODForDistance(float distance) {
    if (distance < 100) return 0;  // 1m resolution
    if (distance < 200) return 1;  // 2m resolution
    if (distance < 400) return 2;  // 4m resolution
    return 3;                       // 8m resolution
}
```

**Vertex Reduction:**
- LOD 0: 289 vertices (17x17)
- LOD 1: 81 vertices (9x9)
- LOD 2: 25 vertices (5x5)
- LOD 3: 9 vertices (3x3)

### Parallel Operations

Where possible, operations are parallelized:

```csharp
// Parallel chunk generation
var chunks = visibleChunks
    .AsParallel()
    .WithDegreeOfParallelism(Environment.ProcessorCount)
    .Select(id => new {
        Id = id,
        Mesh = _generator.GenerateMesh(_dataManager.GetChunk(id))
    })
    .ToList();

// Sequential GPU upload (must be on render thread)
foreach (var chunk in chunks) {
    _gpuManager.UpdateChunk(chunk.Id, chunk.Mesh);
}
```

---

## Extension Points

### Custom Document Types

Create new document types by inheriting `BaseDocument`:

```csharp
public class MyCustomDocument : BaseDocument {
    public MyCustomDocument(ILogger logger) : base(logger) { }

    public override async Task<bool> InitAsync(IDatReaderWriter dats, DocumentManager manager) {
        // Initialize document
        return true;
    }

    public override byte[] SaveToProjection() {
        // Serialize to bytes
        return MemoryPackSerializer.Serialize(new MyProjection());
    }

    public override bool LoadFromProjection(byte[] data) {
        // Deserialize from bytes
        var projection = MemoryPackSerializer.Deserialize<MyProjection>(data);
        // Load data
        return true;
    }
}
```

### Custom Editor Tools

Create new editing tools by inheriting `LandscapeToolViewModelBase`:

```csharp
public class MyCustomToolViewModel : LandscapeToolViewModelBase {
    public MyCustomToolViewModel(TerrainEditingContext context)
        : base(context) {
    }

    public override void OnMouseDown(MouseState state) {
        // Handle mouse down
        var ray = CalculateRay(state.Position);
        var hit = Context.Raycast(ray);

        if (hit.HasValue) {
            var command = new MyCustomCommand(hit.Value);
            Context.History.ExecuteCommand(command);
        }
    }

    public override void OnMouseMove(MouseState state) {
        // Handle mouse move
    }
}
```

### Custom Commands

Implement `ICommand` for undo/redo support:

```csharp
public class MyCustomCommand : ICommand {
    private readonly Vector3 _position;
    private readonly object _oldState;
    private readonly object _newState;

    public string Description => "My Custom Operation";

    public void Execute() {
        // Apply changes
    }

    public void Undo() {
        // Revert changes
    }
}
```

### Custom Storage Providers

Implement `IDocumentStorageService` for alternative storage:

```csharp
public class CloudStorageService : IDocumentStorageService {
    public async Task<DBDocument?> GetDocumentAsync(string documentId) {
        // Load from cloud storage
    }

    public async Task UpdateDocumentAsync(string id, byte[] data) {
        // Save to cloud storage
    }

    // Implement other interface methods
}

// Register in DI
services.AddSingleton<IDocumentStorageService, CloudStorageService>();
```

---

## Testing Strategy

### Unit Tests

Test individual components in isolation:

```csharp
[Fact]
public void CommandHistory_Undo_RevertsLastCommand() {
    var history = new CommandHistory();
    var command = new MockCommand();

    history.ExecuteCommand(command);
    Assert.True(command.Executed);

    history.Undo();
    Assert.True(command.Undone);
}
```

### Integration Tests

Test component interactions:

```csharp
[Fact]
public async Task DocumentManager_SavesChangesInBatch() {
    var storage = new InMemoryStorageService();
    var manager = new DocumentManager(storage, logger);
    var doc = await manager.GetOrCreateDocumentAsync<TerrainDocument>("test");

    // Make 100 changes
    for (int i = 0; i < 100; i++) {
        doc.SetHeight(i, 0, 100f);
    }

    // Wait for batch
    await Task.Delay(3000);

    // Should have batched into single save
    Assert.Equal(1, storage.SaveCount);
}
```

### Performance Tests

Measure performance of critical paths:

```csharp
[Fact]
public void TerrainGeneration_GeneratesChunkUnder10ms() {
    var generator = new TerrainGeometryGenerator();
    var data = CreateTestChunkData();

    var sw = Stopwatch.StartNew();
    var mesh = generator.GenerateMesh(data, lod: 0);
    sw.Stop();

    Assert.True(sw.ElapsedMilliseconds < 10);
}
```

---

## Build and Deployment

### Build Configuration

**Debug:**
- Compiled bindings disabled for better debugging
- Diagnostics enabled
- No AOT compilation

**Release:**
- Compiled bindings enabled
- Diagnostics disabled
- AOT compilation for improved startup
- Trimming enabled for smaller binary

### Deployment Targets

**Windows:**
```bash
dotnet publish -c Release -r win-x64 --self-contained
```

**Linux:**
```bash
dotnet publish -c Release -r linux-x64 --self-contained
```

**macOS:**
```bash
dotnet publish -c Release -r osx-x64 --self-contained
```

**Browser (WebAssembly):**
```bash
dotnet publish WorldBuilder.Browser -c Release
```

### Auto-Update System

NetSparkleUpdater provides automatic updates:

```csharp
var sparkle = new SparkleUpdater(
    "https://chorizite.github.io/WorldBuilder/appcast.xml",
    new Ed25519Checker(SecurityMode.Strict, publicKey)
) {
    UIFactory = new NetSparkleUpdater.UI.Avalonia.UIFactory(),
    RelaunchAfterUpdate = false
};

sparkle.StartLoop(
    doInitialCheck: true,
    checkFrequency: TimeSpan.FromHours(1)
);
```

**AppCast XML:**
```xml
<item>
    <title>WorldBuilder 1.2.0</title>
    <sparkle:version>1.2.0</sparkle:version>
    <sparkle:releaseNotesLink>https://github.com/Chorizite/WorldBuilder/releases/tag/v1.2.0</sparkle:releaseNotesLink>
    <pubDate>Thu, 06 Nov 2025 12:00:00 +0000</pubDate>
    <enclosure url="https://github.com/Chorizite/WorldBuilder/releases/download/v1.2.0/WorldBuilder-Setup.exe"
               sparkle:version="1.2.0"
               sparkle:os="windows"
               type="application/octet-stream"
               sparkle:edSignature="signature_here"/>
</item>
```

---

## See Also

- [README.md](../README.md) - Project overview
- [FEATURES.md](FEATURES.md) - Feature documentation
- [API.md](API.md) - API reference
- [CONTRIBUTING.md](CONTRIBUTING.md) - Contribution guidelines
