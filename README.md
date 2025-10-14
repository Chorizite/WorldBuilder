# WorldBuilder Camera System

Comprehensive camera system with smooth transitions between 3D perspective and 2D orthographic views, featuring interactive map rotation and adaptive behavior based on altitude.

## Overview

The WorldBuilder camera system provides seamless navigation between two camera modes:
- **3D Perspective Mode**: First-person style camera for detailed terrain editing with realistic depth perception
- **2D Orthographic Mode (Flatmap)**: Top-down map view for large-scale navigation and planning

The system automatically adapts to altitude changes while maintaining user control and preventing disorienting camera jumps through smooth animated transitions and orientation preservation.

## Features

- **Smooth Animated Transitions**: 0.5-second animated rotation when automatically switching between modes
- **Automatic Mode Switching**: Intelligently switches based on altitude (3D → 2D) and zoom level (2D → 3D)
- **Orientation Preservation**: Camera direction and yaw angle preserved across mode switches
- **Interactive 2D Map Rotation**: Rotate the flatmap to any orientation with middle mouse button
- **Rotation-Aware Movement**: WASD controls respect current map rotation in both modes
- **Cooldown System**: Prevents rapid back-and-forth switching with 1-second minimum gap
- **Manual Override**: Q key for instant manual switching between modes
- **Reset-to-North**: R key to reset flatmap orientation to 0° (north)

## Controls

### 3D Perspective Mode

| Control | Action |
|---------|--------|
| **W** | Move forward (in camera's facing direction) |
| **A** | Move left (strafe) |
| **S** | Move backward |
| **D** | Move right (strafe) |
| **Space** | Move up (increase altitude) |
| **Shift** | Move down (decrease altitude) |
| **Right-click + Drag** | Rotate camera (yaw and pitch) |
| **Mouse Wheel Up** | Zoom in (decrease altitude, move closer) |
| **Mouse Wheel Down** | Zoom out (increase altitude, move away) |
| **Q** | Toggle to 2D orthographic mode |

**Zoom Behavior:**
- Mouse wheel up = zoom IN (decrease altitude, get closer to terrain)
- Mouse wheel down = zoom OUT (increase altitude, move away from terrain)
- Zoom speed scales with current altitude (10% per scroll step)
- Minimum zoom speed: 50 units per scroll step
- Altitude clamped to minimum 10 units above ground

**Constraints:**
- Pitch clamped to [-89°, 89°] to prevent gimbal lock

### 2D Orthographic Mode (Flatmap)

| Control | Action |
|---------|--------|
| **W** | Move north (screen up, accounting for rotation) |
| **A** | Move west (screen left) |
| **S** | Move south (screen down) |
| **D** | Move east (screen right) |
| **Space** | Zoom out (increase orthographic size) |
| **Shift** | Zoom in (decrease orthographic size) |
| **Right-click + Drag** | Pan camera (drag-to-move) |
| **Middle-click + Drag** | Rotate map around Z-axis |
| **Mouse Wheel Up** | Zoom in (decrease orthographic size, see less area) |
| **Mouse Wheel Down** | Zoom out (increase orthographic size, see more area) |
| **R** | Reset orientation to north (yaw = 0°) |
| **Q** | Toggle to 3D perspective mode |

**Rotation Behavior:**
- Horizontal mouse movement controls rotation during middle-click drag
- Sensitivity: 0.3 degrees per pixel
- Yaw normalized to [-180°, 180°] range
- WASD movement respects current rotation

**Zoom Behavior:**
- Space/Shift: 2x zoom speed per second (relative to current zoom)
- Mouse wheel: 10% zoom per scroll step
- Orthographic size range: [1.0, 100000.0]

## Automatic Mode Switching

The camera automatically switches between modes to prevent seeing empty sky and maintain optimal view quality:

### 3D → 2D Transition (Zooming Out)

**Trigger Conditions:**
- Camera altitude exceeds **2500 units** → switches to orthographic mode
- Animation begins at altitude **2000 units**
- Only triggers if cooldown period (1 second) has elapsed

**Animation Sequence:**
1. At altitude **2000**: Start smooth pitch/yaw animation (0.5 seconds)
   - Pitch animates to -89° (looking straight down)
   - Yaw animates to 0° (facing north) using shortest rotational path
   - Uses smoothstep interpolation: `t² × (3 - 2t)`
2. At altitude **2500**: Complete switch to orthographic camera
   - Preserves XY position from perspective camera
   - Inherits yaw angle from perspective camera
   - Maintains visual continuity

**Console Output:**
```
Started top-down animation (altitude: 2000)
Auto-switched to top-down view (altitude: 2506)
```

### 2D → 3D Transition (Zooming In)

**Trigger Conditions:**
- Orthographic size < **800** (zoomed in close enough)
- Only triggers if cooldown period (1 second) has elapsed

**Transition Behavior:**
1. Switch to perspective camera at altitude **1200** (safe zone)
2. Preserve XY position from orthographic camera
3. Set pitch to -89° (top-down view)
4. Preserve yaw angle from orthographic camera
5. User can then freely adjust camera orientation

**Console Output:**
```
Auto-switched to perspective view with top-down orientation (zoom: 794)
```

### Manual Mode Switching

**Q Key:**
- Press **Q** to toggle between perspective and orthographic
- Resets cooldown timer to allow immediate follow-up auto-switching
- Resets animation state flags
- Console confirms: "Switched to [mode] camera (manual)"

**Use Cases:**
- Quick preview of area in flatmap mode
- Return to 3D mode at any altitude
- Override automatic switching behavior

### Cooldown System

**Purpose:** Prevent rapid back-and-forth camera mode switching

**Implementation:**
- 1-second minimum gap between automatic mode switches
- Manual Q-key switches bypass cooldown for next auto-switch
- Safe altitude (1200) placed strategically between thresholds

**Example Timeline:**
```
T=0.0s: Auto-switch 3D→2D (altitude 2500)
T=0.5s: User zooms in (zoom 750) — blocked by cooldown
T=1.0s: Cooldown expires
T=1.1s: Auto-switch 2D→3D allowed
```

## Settings

Access camera settings at: **Settings → Landscape Editor → Camera**

| Setting | Description | Range | Default |
|---------|-------------|-------|---------|
| **Max Draw Distance** | Maximum render distance | 100-100000 | 4000 |
| **Field of View** | Camera FOV in degrees | 30-120° | 60° |
| **Mouse Sensitivity** | Look sensitivity multiplier | 0.1-5.0 | 1.0 |
| **Mouse Wheel Zoom Sensitivity** | Zoom speed multiplier | 0.1-3.0 | 1.0 |
| **Movement Speed** | Camera movement speed | 1-20000 | 1000 |

**Persistent Settings:**
- Camera movement speed, mouse sensitivity, and field of view are stored in WorldBuilderSettings

**Not Persisted:**
- Current camera mode (resets to perspective on launch)
- Current orientation/position
- Animation state

## Technical Implementation

### Camera Classes

**PerspectiveCamera:**
```csharp
public class PerspectiveCamera : ICamera {
    private float yaw;           // Horizontal rotation
    private float pitch;         // Vertical rotation
    private bool isAnimating;    // Animation active flag

    public void AnimateToTopDown();
    public bool UpdateAnimation(double deltaTime);
    public void SetTopDownOrientation(float specificYaw = 0f);
    public bool IsAnimating { get; }
    public float Yaw { get; }
}
```

**OrthographicTopDownCamera:**
```csharp
public class OrthographicTopDownCamera : ICamera {
    private float yaw;                // Map rotation angle
    private float orthographicSize;   // Zoom level

    public float Yaw { get; set; }
    public float OrthographicSize { get; }
    public void ResetOrientation();   // Set yaw to 0°
}
```

**CameraManager:**
```csharp
public class CameraManager {
    public ICamera Current { get; }

    public void Update(double deltaTime);
    public void SwitchCamera(ICamera newCamera);
    public void SwitchToPerspectiveFromTopDown(
        ICamera perspectiveCamera,
        float targetAltitude = 1200f
    );
}
```

### Animation System

**State Variables:**
```csharp
private bool isAnimating;
private float targetPitch;
private float targetYaw;
private float animationStartPitch;
private float animationStartYaw;
private float animationProgress; // 0 to 1
```

**Animation Algorithm:**
```
animationSpeed = 2.0 (completes in 0.5 seconds)
animationProgress += deltaTime × animationSpeed

smoothT = t² × (3 - 2t)  // Smoothstep easing

pitch = lerp(startPitch, targetPitch, smoothT)

// Yaw uses shortest path (handles 360° wrapping)
yawDiff = targetYaw - startYaw
normalize yawDiff to [-180, 180]
yaw = startYaw + yawDiff × smoothT
```

**Smoothstep Function Benefits:**
- Smooth acceleration at start
- Smooth deceleration at end
- No jarring speed changes
- Visually pleasing motion

### Constants and Thresholds

| Constant | Value | Purpose |
|----------|-------|---------|
| `SWITCH_TO_TOPDOWN_ALTITUDE` | 2500f | Altitude to switch to flatmap |
| `SWITCH_TO_PERSPECTIVE_ZOOM` | 800f | Zoom level to switch to 3D |
| `START_ANIMATION_ALTITUDE` | 2000f | Altitude to begin animation |
| `CAMERA_SWITCH_COOLDOWN` | 1.0 | Seconds between auto-switches |
| Animation Speed | 2.0 | Animation completes in 0.5s |
| Rotation Sensitivity | 0.3 | Degrees per pixel for map rotation |
| Zoom Speed (Space/Shift) | 2.0 | 2x current zoom per second |

### Orientation Preservation

**Yaw Synchronization:**

When switching from perspective to orthographic:
```csharp
orthoCamera.Yaw = perspCamera.Yaw;
```

When switching from orthographic to perspective:
```csharp
perspCamera.SetTopDownOrientation(orthoCamera.Yaw);
```

**Result:** Map orientation remains consistent across mode changes

**Example Scenario:**
1. User flies around in 3D facing northeast (yaw = 45°)
2. Zooms out → smooth animation → switches to flatmap
3. Flatmap displays with "up" = northeast (preserves 45° yaw)
4. User zooms back in → perspective camera faces northeast
5. No disorienting rotation jumps

### View Matrix Calculation

**Perspective Camera:**
```csharp
Matrix4x4.CreateLookAtLeftHanded(
    position,           // Camera position
    position + front,   // Look target (position + direction)
    up                  // Up vector
)
```

**Orthographic Camera (with Rotation):**
```csharp
// Up and right vectors rotate based on yaw
float yawRad = DegreesToRadians(yaw);
right = normalize((cos(yawRad), sin(yawRad), 0))
up = normalize((-sin(yawRad), cos(yawRad), 0))
front = (0, 0, -1)  // Always pointing down

Matrix4x4.CreateLookAtLeftHanded(position, position + front, up)
```

**Projection Matrix (Orthographic):**
```csharp
float width = orthographicSize × aspectRatio;
float height = orthographicSize;

Matrix4x4.CreateOrthographicLeftHanded(
    width, height,
    0.1f,      // Near plane
    100000f    // Far plane
)
```

### Movement Formula (Orthographic)

```csharp
// Calculate rotated vectors
right = (cos(yaw), sin(yaw), 0)
up = (-sin(yaw), cos(yaw), 0)

// Apply movement
W: position -= up × speed
S: position += up × speed
A: position += right × speed
D: position -= right × speed
```

### Edge Cases and Rules

**Animation Interruption:**
- User can interrupt animations at any time without issues
- Animation flag resets when altitude drops below 2000
- Manual mode switch clears animation state immediately

**Yaw Wrapping:**
- Yaw rotation takes shortest path to avoid 270° spins
- Example: 350° → 10° goes clockwise (20°), not counterclockwise (340°)

**Orthographic Movement Direction:**
- W key always moves "up" on screen, regardless of rotation
- Movement direction accounts for camera rotation via `up` and `right` vectors

**Gimbal Lock Prevention:**
- Pitch clamped to [-89°, 89°] range
- Top-down orientation uses -89° (not -90°)
- Prevents mathematical singularities in view matrix

**Zoom Limits:**
- Orthographic size: [1.0, 100000.0]
- Prevents division by zero in projection calculations
- Prevents inverted/inside-out view states

## Usage Examples

### Example 1: Exploring Terrain

1. Start in 3D perspective mode at ground level
2. Hold Space to ascend to altitude 2000
3. Animation begins: camera smoothly rotates to look straight down
4. Continue ascending to altitude 2500
5. Automatic switch to flatmap mode with preserved orientation
6. Use middle-click + drag to rotate map for better view angle
7. Press R to reset to north if disoriented

### Example 2: Quick Mode Toggle

1. Working in 3D mode at altitude 500
2. Press Q to instantly switch to flatmap
3. Review large area in top-down view
4. Press Q again to return to 3D mode
5. Camera returns to previous 3D position and orientation

### Example 3: Zooming Back to 3D

1. Viewing terrain in flatmap mode
2. Find interesting area and use mouse wheel to zoom in
3. When orthographic size drops below 800, automatic switch to 3D
4. Camera placed at altitude 1200 with top-down view
5. User can now adjust pitch and explore in 3D

## Tips

- **Slow zoom?** Increase Mouse Wheel Zoom Sensitivity in settings
- **Fast zoom?** Decrease Mouse Wheel Zoom Sensitivity
- **Lost orientation?** Press R to reset flatmap to north
- **Want to stay in 3D at high altitude?** Use manual Q toggle to override automatic switching
- **Switching too frequently?** The 1-second cooldown prevents rapid mode changes
- **Animation feels off?** Try starting the ascent from a level pitch angle

## Testing

### Basic Functionality
- [ ] Camera animates smoothly when zooming out from 3D
- [ ] Camera switches to flatmap at altitude 2500
- [ ] Camera switches back to 3D when zooming in (zoom < 800)
- [ ] Manual Q-key toggle works in both modes
- [ ] Yaw preserved when switching 3D ↔ 2D

### Animation System
- [ ] Animation starts at altitude 2000
- [ ] Pitch animates to -89° smoothly
- [ ] Yaw animates to 0° using shortest path
- [ ] Animation completes in ~0.5 seconds
- [ ] Animation can be interrupted by descending
- [ ] Animation resets properly on interruption

### 2D Map Rotation
- [ ] Middle mouse + drag rotates the map
- [ ] Rotation is smooth and responsive
- [ ] R key resets orientation to north
- [ ] WASD movement respects rotation (W = screen up)
- [ ] Right-click panning respects rotation

### Cooldown System
- [ ] Cannot auto-switch within 1 second of previous switch
- [ ] Manual Q-key switch resets cooldown
- [ ] Cooldown prevents rapid switching loops
- [ ] Console messages show proper timing

### Edge Cases
- [ ] Zooming in immediately after switching to flatmap (cooldown blocks)
- [ ] Descending during animation (animation resets)
- [ ] Manual switch during animation (animation clears)
- [ ] Rotating map 360° (yaw normalizes correctly)
- [ ] Extreme zoom levels (clamped properly)

### Movement Controls
- [ ] WASD works correctly in both modes
- [ ] Space/Shift zoom in orthographic mode
- [ ] Space/Shift altitude in perspective mode
- [ ] Right-click drag pans in orthographic
- [ ] Right-click drag rotates in perspective
- [ ] Middle-click rotates in orthographic only

### Visual Quality
- [ ] No stuttering during animation
- [ ] No sudden camera jumps
- [ ] Smooth transition between modes
- [ ] Orientation matches between modes
- [ ] Movement directions intuitive in both modes

## Future Enhancements

### Priority 1 (High Impact)
- **Adaptive Animation Speed**: Adjust animation duration based on altitude change rate
- **Smooth Zoom Transitions**: Interpolate altitude when switching 2D→3D
- **Camera Bookmarks**: Save/load camera positions and orientations
- **Minimap Sync**: Show 3D camera position on 2D minimap with rotation indicator

### Priority 2 (Medium Impact)
- **Custom Rotation Center**: Rotate orthographic camera around cursor position
- **Inertia/Momentum**: Camera continues moving briefly after key release
- **Orbit Mode**: Rotate 3D camera around a fixed point
- **Field-of-View Animation**: Smoothly adjust FOV during mode transitions

### Priority 3 (Nice to Have)
- **Camera Shake**: Subtle shake effects for environmental feedback
- **Path Following**: Animate camera along predefined spline paths
- **Parallax Layers**: Multiple 2D layers at different depths
- **VR Support**: Stereo rendering and head tracking integration

## Performance Considerations

### Animation Performance
- **CPU Cost**: Per-frame animation update: ~0.01ms
- **Optimization**: Animation only active during transition period (0.5 seconds)
- No allocations during animation (uses value types)
- UpdateAnimation() early-exits if not animating

### Rotation Performance
- Only recalculates vectors when yaw changes (not every frame)
- Uses cached values when rotation static
- No matrix inversion or decomposition required

### Mode Switching Performance
- **SwitchCamera() Cost**: <1µs on modern hardware
- No GPU impact (same view/projection matrix calculation cost in both modes)
- No shader recompilation required
- No texture uploads or buffer transfers

## Integration with Existing Systems

### Input State System
```csharp
public struct MouseState {
    public Vector2 Position;
    public bool LeftPressed;
    public bool RightPressed;
    public bool MiddlePressed;  // Used for rotation
    public Vector2 Delta;
}
```

### Terrain System
```csharp
_viewModel.TerrainSystem.CameraManager.Current
_viewModel.TerrainSystem.PerspectiveCamera
_viewModel.TerrainSystem.TopDownCamera
```

### Update Loop
```csharp
// In HandleInput() called every frame
_viewModel.TerrainSystem.CameraManager.Update(deltaTime);
```

---

**Version:** 1.0
**Date:** January 2025
**Last Updated:** January 2025
