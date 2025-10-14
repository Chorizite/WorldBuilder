# Rendering Optimization Plan - Issue #3

## Overview

Optimize landscape terrain rendering in orthographic (2D flatmap) mode by switching to a texture-based rendering approach when zoomed out far enough that individual vertex details are not visible.

**GitHub Issue:** [#3](https://github.com/Chorizite/WorldBuilder/issues/3)

## Problem Statement

Currently, the terrain renderer draws all terrain vertices and triangles even when viewing the map from very high altitudes in orthographic mode. At these zoom levels:
- Texture blending details are not visible to the user
- Individual terrain variations are imperceptible
- GPU is wasting cycles rendering detail that can't be seen
- Performance could be improved significantly

## Proposed Solution

Implement a two-stage rendering system that adapts based on zoom level:

### Stage 1: Detailed Terrain Rendering (Close Zoom)
- Current full 3D terrain rendering
- All vertices, triangles, and texture blending active
- Used when user can see terrain details
- Threshold: When orthographic size < **2000 units** (configurable)

### Stage 2: Texture-Based Rendering (Far Zoom)
- Render terrain to an offscreen texture/framebuffer
- Display a single quad with the pre-rendered texture
- Texture updated periodically or when terrain changes
- Used when terrain details are not visible
- Threshold: When orthographic size >= **2000 units** (configurable)

## Technical Implementation

### Architecture Components

```
┌─────────────────────────────────────┐
│     TerrainRenderer                  │
│                                      │
│  ┌────────────────────────────────┐ │
│  │  RenderModeController           │ │
│  │  - Monitors zoom level          │ │
│  │  - Switches between modes       │ │
│  └────────────────────────────────┘ │
│                                      │
│  ┌────────────────────────────────┐ │
│  │  DetailedTerrainRenderer        │ │
│  │  - Current 3D terrain rendering │ │
│  └────────────────────────────────┘ │
│                                      │
│  ┌────────────────────────────────┐ │
│  │  TextureBasedRenderer           │ │
│  │  - Framebuffer rendering        │ │
│  │  - Quad rendering               │ │
│  │  - Cache management             │ │
│  └────────────────────────────────┘ │
└─────────────────────────────────────┘
```

### Class Structure

#### 1. RenderModeController
```csharp
public class RenderModeController {
    private const float TEXTURE_MODE_THRESHOLD = 2000f;
    private const float HYSTERESIS = 200f; // Prevent rapid switching

    public enum RenderMode {
        Detailed,    // Full 3D rendering
        Texture      // Single quad with texture
    }

    private RenderMode currentMode = RenderMode.Detailed;
    private RenderMode pendingMode = RenderMode.Detailed;

    public RenderMode DetermineRenderMode(OrthographicTopDownCamera camera) {
        float orthoSize = camera.OrthographicSize;

        // Hysteresis to prevent rapid mode switching
        if (currentMode == RenderMode.Detailed) {
            if (orthoSize > TEXTURE_MODE_THRESHOLD + HYSTERESIS) {
                pendingMode = RenderMode.Texture;
            }
        } else {
            if (orthoSize < TEXTURE_MODE_THRESHOLD - HYSTERESIS) {
                pendingMode = RenderMode.Detailed;
            }
        }

        currentMode = pendingMode;
        return currentMode;
    }
}
```

#### 2. TextureBasedRenderer
```csharp
public class TextureBasedRenderer : IDisposable {
    private GL gl;
    private uint framebuffer;
    private uint terrainTexture;
    private uint quadVAO;
    private uint quadVBO;
    private ShaderProgram quadShader;

    private bool textureNeedsUpdate = true;
    private int textureResolution = 4096; // High-res texture

    public TextureBasedRenderer(GL gl) {
        this.gl = gl;
        CreateFramebuffer();
        CreateQuad();
        LoadShaders();
    }

    private void CreateFramebuffer() {
        // Create framebuffer for offscreen rendering
        framebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);

        // Create texture to render to
        terrainTexture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, terrainTexture);
        gl.TexImage2D(
            TextureTarget.Texture2D,
            0,
            InternalFormat.Rgb,
            textureResolution,
            textureResolution,
            0,
            PixelFormat.Rgb,
            PixelType.UnsignedByte,
            null
        );

        gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

        // Attach texture to framebuffer
        gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            terrainTexture,
            0
        );

        // Check framebuffer completeness
        if (gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete) {
            throw new Exception("Framebuffer is not complete!");
        }

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void CreateQuad() {
        // Create a simple fullscreen quad
        float[] quadVertices = {
            // positions        // texCoords
            -1.0f,  1.0f, 0.0f, 0.0f, 1.0f,
            -1.0f, -1.0f, 0.0f, 0.0f, 0.0f,
             1.0f, -1.0f, 0.0f, 1.0f, 0.0f,
             1.0f,  1.0f, 0.0f, 1.0f, 1.0f
        };

        quadVAO = gl.GenVertexArray();
        quadVBO = gl.GenBuffer();

        gl.BindVertexArray(quadVAO);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, quadVBO);
        gl.BufferData(BufferTargetARB.ArrayBuffer, quadVertices, BufferUsageARB.StaticDraw);

        // Position attribute
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), (void*)0);
        gl.EnableVertexAttribArray(0);

        // TexCoord attribute
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), (void*)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(1);

        gl.BindVertexArray(0);
    }

    public void RenderTerrainToTexture(TerrainSystem terrainSystem, Matrix4x4 viewMatrix, Matrix4x4 projMatrix) {
        if (!textureNeedsUpdate) return;

        // Bind framebuffer for offscreen rendering
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        gl.Viewport(0, 0, textureResolution, textureResolution);
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        // Render terrain using existing terrain renderer
        // (This would use the detailed terrain rendering code)
        // terrainSystem.Renderer.RenderToCurrentFramebuffer(viewMatrix, projMatrix);

        // Unbind framebuffer
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        textureNeedsUpdate = false;
    }

    public void RenderQuad() {
        quadShader.Use();
        gl.BindTexture(TextureTarget.Texture2D, terrainTexture);
        gl.BindVertexArray(quadVAO);
        gl.DrawArrays(PrimitiveType.TriangleFan, 0, 4);
        gl.BindVertexArray(0);
    }

    public void MarkDirty() {
        textureNeedsUpdate = true;
    }

    public void Dispose() {
        gl.DeleteFramebuffer(framebuffer);
        gl.DeleteTexture(terrainTexture);
        gl.DeleteVertexArray(quadVAO);
        gl.DeleteBuffer(quadVBO);
        quadShader?.Dispose();
    }
}
```

#### 3. Integration into TerrainRenderer

```csharp
public class TerrainRenderer {
    private RenderModeController modeController;
    private TextureBasedRenderer textureRenderer;

    public void Render(ICamera camera, ...) {
        if (camera is OrthographicTopDownCamera orthoCamera) {
            var renderMode = modeController.DetermineRenderMode(orthoCamera);

            if (renderMode == RenderMode.Texture) {
                // Render to texture (if needed)
                textureRenderer.RenderTerrainToTexture(terrainSystem, viewMatrix, projMatrix);

                // Render quad with texture
                textureRenderer.RenderQuad();
            } else {
                // Standard detailed rendering
                RenderDetailedTerrain(camera);
            }
        } else {
            // Always use detailed rendering for perspective camera
            RenderDetailedTerrain(camera);
        }
    }
}
```

## Cache Invalidation Strategy

The texture cache needs to be invalidated when:

1. **Terrain Modifications**: Any height or texture changes
2. **Camera Position Changes**: When panning significantly (chunk-based)
3. **Camera Rotation Changes**: When map rotation changes significantly
4. **Manual Refresh**: User-triggered refresh if needed

### Intelligent Cache Updates

```csharp
public class TextureCacheManager {
    private Vector2 lastRenderPosition;
    private float lastRenderYaw;
    private const float POSITION_THRESHOLD = 500f; // Units of movement before refresh
    private const float YAW_THRESHOLD = 30f; // Degrees of rotation before refresh

    public bool ShouldUpdate(Vector3 currentPosition, float currentYaw) {
        Vector2 currentPos2D = new Vector2(currentPosition.X, currentPosition.Y);
        float distance = Vector2.Distance(currentPos2D, lastRenderPosition);
        float yawDiff = Math.Abs(currentYaw - lastRenderYaw);

        if (distance > POSITION_THRESHOLD || yawDiff > YAW_THRESHOLD) {
            lastRenderPosition = currentPos2D;
            lastRenderYaw = currentYaw;
            return true;
        }

        return false;
    }
}
```

## Performance Considerations

### Expected Performance Gains

**Before Optimization (Zoomed Out):**
- Rendering ~100-200 terrain chunks
- ~500k-1M vertices
- ~1-2M triangles
- Multiple texture binds per chunk
- Estimated: 30-60 FPS at far zoom

**After Optimization (Zoomed Out):**
- Rendering 1 quad
- 4 vertices
- 2 triangles
- 1 texture bind
- Estimated: 144+ FPS (VSync limited)

### Memory Usage

- Framebuffer: 4096x4096 RGB = ~48 MB
- Additional GPU memory negligible
- Trade-off: Small memory increase for massive performance gain

### Texture Resolution Options

| Resolution | Memory | Quality | Recommended For |
|------------|--------|---------|-----------------|
| 2048x2048 | 12 MB | Medium | Lower-end GPUs |
| 4096x4096 | 48 MB | High | Modern GPUs |
| 8192x8192 | 192 MB | Ultra | High-end GPUs with 4K+ displays |

## Settings Integration

Add to LandscapeEditorSettings:

```csharp
[SettingCategory("Performance", ParentCategory = "Landscape Editor", Order = 3)]
public partial class PerformanceSettings : ObservableObject {
    [SettingDescription("Enable texture-based rendering when zoomed out")]
    [SettingOrder(1)]
    private bool _enableTextureMode = true;
    public bool EnableTextureMode { get => _enableTextureMode; set => SetProperty(ref _enableTextureMode, value); }

    [SettingDescription("Zoom threshold for texture mode (larger = switches sooner)")]
    [SettingRange(1000, 5000, 100, 200)]
    [SettingFormat("{0:F0}")]
    [SettingOrder(2)]
    private float _textureModeThreshold = 2000f;
    public float TextureModeThreshold { get => _textureModeThreshold; set => SetProperty(ref _textureModeThreshold, value); }

    [SettingDescription("Texture resolution (higher = better quality, more memory)")]
    [SettingOrder(3)]
    private int _textureResolution = 4096;
    public int TextureResolution {
        get => _textureResolution;
        set {
            // Clamp to power of 2
            int clamped = (int)Math.Pow(2, Math.Round(Math.Log(value, 2)));
            SetProperty(ref _textureResolution, clamped);
        }
    }
}
```

## Implementation Phases

### Phase 1: Infrastructure (Week 1)
- [ ] Create RenderModeController class
- [ ] Add texture mode threshold settings
- [ ] Implement mode switching logic with hysteresis
- [ ] Add telemetry/debugging output

### Phase 2: Texture Rendering (Week 2)
- [ ] Create TextureBasedRenderer class
- [ ] Implement framebuffer creation
- [ ] Create quad geometry and shaders
- [ ] Test offscreen terrain rendering

### Phase 3: Integration (Week 3)
- [ ] Integrate into TerrainRenderer
- [ ] Wire up camera-based mode detection
- [ ] Implement cache invalidation
- [ ] Add terrain modification listeners

### Phase 4: Optimization & Polish (Week 4)
- [ ] Implement intelligent cache updates
- [ ] Add position/rotation-based refresh logic
- [ ] Performance testing and profiling
- [ ] User testing for visual quality
- [ ] Settings UI integration
- [ ] Documentation

## Testing Checklist

### Functional Testing
- [ ] Mode switches correctly at threshold
- [ ] Hysteresis prevents rapid switching
- [ ] Texture updates when terrain modified
- [ ] Texture updates when camera moves significantly
- [ ] Texture updates when camera rotates significantly
- [ ] No visual artifacts during transitions
- [ ] Works with terrain painting
- [ ] Works with height sculpting

### Performance Testing
- [ ] Measure FPS before/after at far zoom
- [ ] Measure GPU memory usage
- [ ] Profile texture update frequency
- [ ] Test on various GPU tiers
- [ ] Verify no memory leaks

### Visual Quality Testing
- [ ] Texture resolution adequate at various zoom levels
- [ ] No aliasing issues
- [ ] Colors match detailed rendering
- [ ] Smooth transition between modes
- [ ] Rotation rendering correct

## Risks & Mitigation

| Risk | Impact | Mitigation |
|------|---------|-----------|
| Texture quality insufficient | Medium | Make resolution configurable, default to 4096 |
| Too frequent texture updates | High | Implement intelligent caching with thresholds |
| Memory constraints on low-end GPUs | Medium | Add lower resolution options, allow disable |
| Visual artifacts during transition | Medium | Implement smooth fade transition |
| Increased code complexity | Low | Keep components modular and well-documented |

## Future Enhancements

1. **Progressive Texture Streaming**: Load higher-resolution textures progressively as user zooms
2. **Multiple LOD Textures**: Pre-render textures at multiple zoom levels
3. **Async Texture Updates**: Update texture in background thread
4. **Viewport-Based Rendering**: Only render visible portion of map to texture
5. **Texture Compression**: Use DXT/BC compression to reduce memory usage

## Success Metrics

- **Performance**: 2-3x FPS improvement when zoomed out far
- **Memory**: < 100 MB additional GPU memory usage
- **Quality**: No visible quality degradation at typical zoom levels
- **User Satisfaction**: Smooth, responsive experience at all zoom levels

---

**Status**: Planning
**Priority**: Medium (Quality of Life improvement)
**Estimated Effort**: 3-4 weeks
**Dependencies**: None
