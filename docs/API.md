# WorldBuilder API Reference

Comprehensive API reference for WorldBuilder developers.

## Table of Contents

- [Document System](#document-system)
- [Command System](#command-system)
- [Terrain System](#terrain-system)
- [Rendering System](#rendering-system)
- [Editor Tools](#editor-tools)
- [Utilities](#utilities)

---

## Document System

### BaseDocument

Abstract base class for all document types.

```csharp
namespace WorldBuilder.Shared.Documents;

public abstract class BaseDocument
{
    // Properties
    public string Id { get; set; }
    public bool IsDirty { get; protected set; }

    // Events
    public event EventHandler<UpdateEventArgs>? Update;

    // Abstract Methods
    public abstract Task<bool> InitAsync(IDatReaderWriter dats, DocumentManager manager);
    public abstract byte[] SaveToProjection();
    public abstract bool LoadFromProjection(byte[] data);

    // Protected Methods
    protected void NotifyUpdate();
    protected void MarkDirty();
    public void SetCacheDirectory(string cacheDirectory);
}
```

**Usage Example:**
```csharp
public class MyDocument : BaseDocument
{
    public MyDocument(ILogger<MyDocument> logger) : base(logger) { }

    public override async Task<bool> InitAsync(IDatReaderWriter dats, DocumentManager manager)
    {
        // Initialize document data
        return true;
    }

    public override byte[] SaveToProjection()
    {
        var projection = new MyDocumentProjection { /* ... */ };
        return MemoryPackSerializer.Serialize(projection);
    }

    public override bool LoadFromProjection(byte[] data)
    {
        var projection = MemoryPackSerializer.Deserialize<MyDocumentProjection>(data);
        // Restore state from projection
        return true;
    }
}
```

### DocumentManager

Manages document lifecycle and persistence.

```csharp
namespace WorldBuilder.Shared.Documents;

public class DocumentManager : IDisposable
{
    // Properties
    public Guid ClientId { get; }
    public IDocumentStorageService DocumentStorageService { get; }
    public IDatReaderWriter Dats { get; set; }
    public ConcurrentDictionary<string, BaseDocument> ActiveDocs { get; }

    // Constructor
    public DocumentManager(
        IDocumentStorageService documentService,
        ILogger<DocumentManager> logger);

    // Methods
    public Task<T?> GetOrCreateDocumentAsync<T>(string documentId)
        where T : BaseDocument;

    public Task<BaseDocument?> GetOrCreateDocumentAsync(
        string documentId,
        Type docType);

    public Task CloseDocumentAsync(string documentId);

    public Task FlushPendingUpdatesAsync();

    public void SetCacheDirectory(string cacheDirectory);

    public void Dispose();
}
```

**Usage Example:**
```csharp
// Get or create a terrain document
var terrain = await documentManager.GetOrCreateDocumentAsync<TerrainDocument>("terrain");

// Access document
terrain.SetHeight(10, 20, 150.0f);

// Document auto-saves via batching

// Close when done
await documentManager.CloseDocumentAsync("terrain");
```

### TerrainDocument

Manages terrain height and texture data.

```csharp
namespace WorldBuilder.Shared.Documents;

public class TerrainDocument : BaseDocument
{
    // Constructor
    public TerrainDocument(ILogger<TerrainDocument> logger);

    // Methods
    public float GetHeight(int x, int z);
    public void SetHeight(int x, int z, float height);

    public byte GetTexture(int x, int z);
    public void SetTexture(int x, int z, byte textureId);

    public Vector3 GetNormal(int x, int z);

    public TerrainChunkData? GetChunk(ulong chunkId);
    public IEnumerable<ulong> GetDirtyChunks();
    public void ClearDirtyChunks();
}
```

**Usage Example:**
```csharp
var terrain = await documentManager.GetOrCreateDocumentAsync<TerrainDocument>("terrain");

// Get height at position
float height = terrain.GetHeight(100, 200);

// Set height
terrain.SetHeight(100, 200, 150.0f);

// Get texture at position
byte textureId = terrain.GetTexture(100, 200);

// Paint texture
terrain.SetTexture(100, 200, 5); // textureId 5
```

### LandblockDocument

Manages landblock-specific data and objects.

```csharp
namespace WorldBuilder.Shared.Documents;

public class LandblockDocument : BaseDocument
{
    // Properties
    public uint LandblockId { get; }

    // Constructor
    public LandblockDocument(ILogger<LandblockDocument> logger);

    // Methods
    public void AddStaticObject(StaticObject obj);
    public void RemoveStaticObject(uint objectId);
    public IEnumerable<StaticObject> GetStaticObjects();
    public IEnumerable<(Vector3 Pos, Quaternion Rot)> GetStaticSpawns();
}
```

### IDocumentStorageService

Interface for document persistence.

```csharp
namespace WorldBuilder.Shared.Documents;

public interface IDocumentStorageService : IDisposable
{
    Task<DBDocument?> GetDocumentAsync(string documentId);

    Task<DBDocument> CreateDocumentAsync(
        string id,
        string type,
        byte[] data);

    Task UpdateDocumentAsync(string id, byte[] data);

    Task DeleteDocumentAsync(string documentId);

    Task<DBSnapshot?> GetSnapshotAsync(string snapshotId);

    Task<List<DBSnapshot>> GetDocumentSnapshotsAsync(string documentId);

    Task SaveSnapshotAsync(DBSnapshot snapshot);

    Task DeleteSnapshotAsync(string snapshotId);
}
```

---

## Command System

### ICommand

Interface for all undoable commands.

```csharp
namespace WorldBuilder.Lib.History;

public interface ICommand
{
    string Description { get; }
    void Execute();
    void Undo();
}
```

**Implementation Example:**
```csharp
public class TerrainHeightCommand : ICommand
{
    private readonly TerrainDocument _document;
    private readonly int _x, _z;
    private readonly float _oldHeight, _newHeight;

    public string Description => $"Set Height ({_x}, {_z})";

    public TerrainHeightCommand(
        TerrainDocument document,
        int x, int z,
        float oldHeight, float newHeight)
    {
        _document = document;
        _x = x;
        _z = z;
        _oldHeight = oldHeight;
        _newHeight = newHeight;
    }

    public void Execute()
    {
        _document.SetHeight(_x, _z, _newHeight);
    }

    public void Undo()
    {
        _document.SetHeight(_x, _z, _oldHeight);
    }
}
```

### CommandHistory

Manages command undo/redo stacks.

```csharp
namespace WorldBuilder.Lib.History;

public class CommandHistory : IDisposable
{
    // Properties
    public bool CanUndo { get; }
    public bool CanRedo { get; }
    public int MaxHistorySize { get; set; } = 50;

    // Events
    public event EventHandler? HistoryChanged;
    public event EventHandler<TrimHistoryEventArgs>? TrimHistory;

    // Methods
    public void ExecuteCommand(ICommand command);
    public void Undo();
    public void Redo();
    public void Clear();

    public IEnumerable<HistoryEntry> GetHistory();
    public HistoryEntry? GetCurrentEntry();
    public void JumpToEntry(HistoryEntry entry);
}
```

**Usage Example:**
```csharp
var history = new CommandHistory();

// Execute command
var command = new PaintCommand(terrain, x, z, oldTex, newTex);
history.ExecuteCommand(command);

// Undo
if (history.CanUndo)
    history.Undo();

// Redo
if (history.CanRedo)
    history.Redo();

// Get history list
var entries = history.GetHistory();
```

### CompositeCommand

Groups multiple commands into one.

```csharp
namespace WorldBuilder.Lib.History;

public class CompositeCommand : ICommand
{
    public string Description { get; set; }

    public CompositeCommand(string description);

    public void AddCommand(ICommand command);
    public void Execute();
    public void Undo();
}
```

**Usage Example:**
```csharp
var composite = new CompositeCommand("Paint Multiple");

for (int i = 0; i < 10; i++)
{
    var cmd = new PaintCommand(terrain, x + i, z, oldTex, newTex);
    composite.AddCommand(cmd);
}

history.ExecuteCommand(composite);
// All 10 paint operations undo/redo as one
```

---

## Terrain System

### TerrainSystem

Main coordinator for terrain operations.

```csharp
namespace WorldBuilder.Editors.Landscape;

public class TerrainSystem : EditorBase
{
    // Properties
    public WorldBuilderSettings Settings { get; }
    public TerrainDocument TerrainDoc { get; }
    public TerrainEditingContext EditingContext { get; }
    public GameScene Scene { get; }
    public IServiceProvider Services { get; }

    // Constructor
    public TerrainSystem(
        OpenGLRenderer renderer,
        Project project,
        IDatReaderWriter dats,
        WorldBuilderSettings settings,
        ILogger<TerrainSystem> logger);

    // Methods
    public void Update(Vector3 cameraPosition, Matrix4x4 viewProjectionMatrix);

    public IEnumerable<StaticObject> GetAllStaticObjects();
    public IEnumerable<(Vector3 Pos, Quaternion Rot)> GetAllStaticSpawns();

    public void RegenerateChunks(IEnumerable<ulong> chunkIds);
    public void UpdateLandblocks(IEnumerable<uint> landblockIds);

    public int GetLoadedChunkCount();
    public int GetVisibleChunkCount(Frustum frustum);
}
```

### TerrainEditingContext

Provides editing APIs to tools.

```csharp
namespace WorldBuilder.Editors.Landscape;

public class TerrainEditingContext
{
    // Properties
    public TerrainSystem TerrainSystem { get; }
    public DocumentManager DocumentManager { get; }
    public CommandHistory History { get; }

    // Constructor
    public TerrainEditingContext(
        DocumentManager documentManager,
        TerrainSystem terrainSystem);

    // Methods
    public RaycastHit? Raycast(Ray ray);

    public void PaintTexture(int x, int z, byte textureId);
    public void SetHeight(int x, int z, float height);

    public IEnumerable<(int x, int z)> GetBrushArea(
        Vector3 center,
        float radius);
}
```

### TerrainDataManager

Manages height and texture data.

```csharp
namespace WorldBuilder.Editors.Landscape;

public class TerrainDataManager
{
    // Constructor
    public TerrainDataManager(TerrainDocument document);

    // Methods
    public float GetHeight(int x, int z);
    public void SetHeight(int x, int z, float height);

    public byte GetTexture(int x, int z);
    public void SetTexture(int x, int z, byte textureId);

    public Vector3 GetNormal(int x, int z);

    public TerrainChunkData GetChunk(ulong chunkId);
    public IEnumerable<ulong> GetDirtyChunks();
    public void ClearDirtyFlag(ulong chunkId);
}
```

### TerrainGeometryGenerator

Generates render meshes from terrain data.

```csharp
namespace WorldBuilder.Editors.Landscape;

public class TerrainGeometryGenerator
{
    // Methods
    public ChunkMeshData GenerateMesh(
        TerrainChunkData data,
        int lod);

    public void RegenerateNormals(TerrainChunkData data);

    public ChunkRenderData CreateRenderData(
        ulong chunkId,
        TerrainChunkData data,
        int lod);
}
```

**Usage Example:**
```csharp
var generator = new TerrainGeometryGenerator();
var chunkData = dataManager.GetChunk(chunkId);

// Generate mesh at LOD 0 (highest detail)
var mesh = generator.GenerateMesh(chunkData, lod: 0);

// Upload to GPU
gpuManager.UpdateChunk(chunkId, mesh);
```

### TerrainRaycast

Performs raycasting against terrain.

```csharp
namespace WorldBuilder.Editors.Landscape;

public class TerrainRaycast
{
    // Constructor
    public TerrainRaycast(TerrainDataManager dataManager);

    // Methods
    public RaycastHit? Raycast(Ray ray);
    public RaycastHit? RaycastChunk(Ray ray, ulong chunkId);
}
```

**Usage Example:**
```csharp
var raycast = new TerrainRaycast(dataManager);
var ray = Camera.ScreenPointToRay(mousePosition);
var hit = raycast.Raycast(ray);

if (hit.HasValue)
{
    Vector3 position = hit.Value.Position;
    Vector3 normal = hit.Value.Normal;
    // Use hit information
}
```

---

## Rendering System

### OpenGLRenderer

Main rendering coordinator.

```csharp
namespace Chorizite.OpenGLSDLBackend;

public class OpenGLRenderer : IDisposable
{
    // Properties
    public int Width { get; }
    public int Height { get; }
    public DrawList2 DrawList { get; }

    // Constructor
    public OpenGLRenderer(int width, int height);

    // Methods
    public void BeginFrame();
    public void EndFrame();
    public void Clear(ColorARGB color);

    public void SetViewport(int x, int y, int width, int height);
    public void SetViewProjection(Matrix4x4 viewProjection);

    public ManagedGLTexture CreateTexture(
        int width, int height,
        TextureFormat format);

    public ManagedGLVertexBuffer CreateVertexBuffer();
    public ManagedGLIndexBuffer CreateIndexBuffer();

    public GLSLShader CreateShader(string vertSource, string fragSource);
}
```

### DrawList2

Command buffer for draw calls.

```csharp
namespace Chorizite.OpenGLSDLBackend;

public class DrawList2
{
    // Methods
    public void DrawTriangles(
        ManagedGLVertexBuffer vertices,
        ManagedGLIndexBuffer indices,
        GLSLShader shader,
        ManagedGLTexture? texture = null);

    public void DrawQuad(
        Vector3 position,
        Vector2 size,
        ColorARGB color);

    public void DrawLine(
        Vector3 start,
        Vector3 end,
        ColorARGB color,
        float thickness = 1.0f);

    public void DrawText(
        string text,
        Vector2 position,
        ColorARGB color,
        float fontSize = 16.0f);

    public void Clear();
}
```

### ManagedGLTexture

Managed OpenGL texture.

```csharp
namespace Chorizite.OpenGLSDLBackend;

public class ManagedGLTexture : IDisposable
{
    // Properties
    public int Handle { get; }
    public int Width { get; }
    public int Height { get; }
    public TextureFormat Format { get; }

    // Methods
    public void SetData<T>(T[] data) where T : struct;
    public void SetData<T>(T[] data, int level) where T : struct;

    public void Bind(int unit = 0);
    public void Unbind();

    public void Dispose();
}
```

### ManagedGLVertexBuffer

Managed OpenGL vertex buffer.

```csharp
namespace Chorizite.OpenGLSDLBackend;

public class ManagedGLVertexBuffer : IDisposable
{
    // Properties
    public int Handle { get; }
    public int VertexCount { get; }

    // Methods
    public void SetData<T>(T[] data, BufferUsage usage = BufferUsage.StaticDraw)
        where T : struct;

    public void Bind();
    public void Unbind();

    public void Dispose();
}
```

### GLSLShader

GLSL shader program.

```csharp
namespace Chorizite.OpenGLSDLBackend;

public class GLSLShader : IDisposable
{
    // Properties
    public int Handle { get; }

    // Constructor
    public GLSLShader(string vertSource, string fragSource);

    // Methods
    public void Use();

    public void SetUniform(string name, int value);
    public void SetUniform(string name, float value);
    public void SetUniform(string name, Vector2 value);
    public void SetUniform(string name, Vector3 value);
    public void SetUniform(string name, Vector4 value);
    public void SetUniform(string name, Matrix4x4 value);
    public void SetUniform(string name, ColorARGB value);

    public int GetUniformLocation(string name);
    public int GetAttribLocation(string name);

    public void Dispose();
}
```

---

## Editor Tools

### LandscapeToolViewModelBase

Base class for all landscape editing tools.

```csharp
namespace WorldBuilder.Editors.Landscape.ViewModels;

public abstract class LandscapeToolViewModelBase : ViewModelBase
{
    // Properties
    protected TerrainEditingContext Context { get; }
    public abstract string Name { get; }
    public abstract string Icon { get; }

    // Constructor
    protected LandscapeToolViewModelBase(TerrainEditingContext context);

    // Virtual Methods
    public virtual void OnActivated() { }
    public virtual void OnDeactivated() { }

    public virtual void OnMouseDown(MouseState state) { }
    public virtual void OnMouseUp(MouseState state) { }
    public virtual void OnMouseMove(MouseState state) { }
    public virtual void OnKeyDown(KeyEventArgs e) { }
    public virtual void OnKeyUp(KeyEventArgs e) { }

    public virtual void Update(float deltaTime) { }
    public virtual void Render(DrawList2 drawList) { }
}
```

**Implementation Example:**
```csharp
public class MyCustomToolViewModel : LandscapeToolViewModelBase
{
    public override string Name => "My Tool";
    public override string Icon => "🔧";

    public MyCustomToolViewModel(TerrainEditingContext context)
        : base(context) { }

    public override void OnMouseDown(MouseState state)
    {
        if (state.LeftButton)
        {
            var ray = CalculateRay(state.Position);
            var hit = Context.Raycast(ray);

            if (hit.HasValue)
            {
                var command = new MyCommand(hit.Value.Position);
                Context.History.ExecuteCommand(command);
            }
        }
    }
}
```

### TexturePaintingToolViewModel

Texture painting tool.

```csharp
namespace WorldBuilder.Editors.Landscape.ViewModels;

public class TexturePaintingToolViewModel : LandscapeToolViewModelBase
{
    // Properties
    public byte SelectedTextureId { get; set; }
    public float BrushSize { get; set; }
    public float BrushStrength { get; set; }

    // Constructor
    public TexturePaintingToolViewModel(
        TerrainEditingContext context,
        WorldBuilderSettings settings);

    // Sub-tools
    public BrushSubToolViewModel BrushTool { get; }
    public BucketFillSubToolViewModel BucketFillTool { get; }
}
```

### RoadDrawingToolViewModel

Road drawing tool.

```csharp
namespace WorldBuilder.Editors.Landscape.ViewModels;

public class RoadDrawingToolViewModel : LandscapeToolViewModelBase
{
    // Properties
    public int SelectedRoadType { get; set; }
    public float RoadWidth { get; set; }

    // Constructor
    public RoadDrawingToolViewModel(
        TerrainEditingContext context);

    // Sub-tools
    public RoadLineSubToolViewModel LineTool { get; }
    public RoadPointSubToolViewModel PointTool { get; }
    public RoadRemoveSubToolViewModel RemoveTool { get; }
}
```

---

## Utilities

### Camera

Camera abstraction for viewport navigation.

```csharp
namespace WorldBuilder.Lib;

public abstract class Camera
{
    // Properties
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }
    public float AspectRatio { get; set; }

    // Methods
    public abstract Matrix4x4 GetViewMatrix();
    public abstract Matrix4x4 GetProjectionMatrix();
    public Matrix4x4 GetViewProjectionMatrix();

    public Ray ScreenPointToRay(Vector2 screenPos);
    public Vector3 ScreenToWorldPoint(Vector2 screenPos, float depth);

    public void Move(Vector3 delta);
    public void Rotate(float pitch, float yaw);
}

public class PerspectiveCamera : Camera
{
    public float FieldOfView { get; set; } = 60f;
    public float NearPlane { get; set; } = 0.1f;
    public float FarPlane { get; set; } = 1000f;
}

public class OrthographicTopDownCamera : Camera
{
    public float OrthographicSize { get; set; } = 100f;
    public float NearPlane { get; set; } = -100f;
    public float FarPlane { get; set; } = 100f;
}
```

### Frustum

View frustum for culling.

```csharp
namespace WorldBuilder.Lib;

public struct Frustum
{
    // Methods
    public static Frustum FromViewProjection(Matrix4x4 viewProjection);

    public bool Intersects(BoundingBox box);
    public bool Intersects(BoundingSphere sphere);
    public bool Contains(Vector3 point);
}
```

### MouseState

Mouse input state.

```csharp
namespace WorldBuilder.Lib;

public class MouseState
{
    public Vector2 Position { get; set; }
    public Vector2 Delta { get; set; }

    public bool LeftButton { get; set; }
    public bool RightButton { get; set; }
    public bool MiddleButton { get; set; }

    public float ScrollDelta { get; set; }

    public bool Shift { get; set; }
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
}
```

### WorldBuilderSettings

Application settings.

```csharp
namespace WorldBuilder.Lib.Settings;

public class WorldBuilderSettings : ObservableObject
{
    // DAT File Paths
    public string CellDatPath { get; set; }
    public string PortalDatPath { get; set; }
    public string HighResDatPath { get; set; }

    // Landscape Editor Settings
    public LandscapeEditorSettings LandscapeEditor { get; set; }
}

public class LandscapeEditorSettings : ObservableObject
{
    public float BrushSize { get; set; } = 5.0f;
    public float BrushStrength { get; set; } = 0.5f;
    public bool ShowGrid { get; set; } = true;
    public float GridSize { get; set; } = 1.0f;
}
```

---

## Type Definitions

### Common Structures

```csharp
public struct Ray
{
    public Vector3 Origin { get; set; }
    public Vector3 Direction { get; set; }
}

public struct RaycastHit
{
    public Vector3 Position { get; set; }
    public Vector3 Normal { get; set; }
    public float Distance { get; set; }
    public ulong ChunkId { get; set; }
}

public struct BoundingBox
{
    public Vector3 Min { get; set; }
    public Vector3 Max { get; set; }

    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 Size => Max - Min;
}

public struct BoundingSphere
{
    public Vector3 Center { get; set; }
    public float Radius { get; set; }
}

public struct ColorARGB
{
    public byte A { get; set; }
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }

    public static ColorARGB FromRgb(byte r, byte g, byte b) =>
        new ColorARGB { A = 255, R = r, G = g, B = b };
}

public struct VertexPositionNormalTexture
{
    public Vector3 Position { get; set; }
    public Vector3 Normal { get; set; }
    public Vector2 TexCoord { get; set; }
}
```

---

## Extension Methods

### ServiceCollectionExtensions

```csharp
namespace WorldBuilder.Lib.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCommonServices(
        this IServiceCollection services)
    {
        services.AddSingleton<ProjectManager>();
        services.AddSingleton<WorldBuilderSettings>();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        return services;
    }
}
```

### ColorARGBExtensions

```csharp
namespace WorldBuilder.Shared.Lib.Extensions;

public static class ColorARGBExtensions
{
    public static Vector4 ToVector4(this ColorARGB color) =>
        new Vector4(
            color.R / 255f,
            color.G / 255f,
            color.B / 255f,
            color.A / 255f);

    public static ColorARGB FromVector4(Vector4 vector) =>
        new ColorARGB
        {
            R = (byte)(vector.X * 255),
            G = (byte)(vector.Y * 255),
            B = (byte)(vector.Z * 255),
            A = (byte)(vector.W * 255)
        };
}
```

---

## See Also

- [README.md](../README.md) - Project overview
- [FEATURES.md](FEATURES.md) - Feature documentation
- [ARCHITECTURE.md](ARCHITECTURE.md) - Architecture details
- [GitHub Wiki](https://github.com/Chorizite/WorldBuilder/wiki) - Online documentation
