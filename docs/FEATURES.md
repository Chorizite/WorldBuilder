# WorldBuilder Features

Comprehensive feature documentation for the WorldBuilder application.

## Table of Contents

- [History and Snapshot System](#history-and-snapshot-system)
- [Layers and Groups Management](#layers-and-groups-management)
- [Terrain Editing](#terrain-editing)
- [Texture Painting](#texture-painting)
- [Road Drawing System](#road-drawing-system)
- [Static Object Management](#static-object-management)
- [Project Management](#project-management)

---

## History and Snapshot System

The History and Snapshot Panel provides comprehensive undo/redo functionality and state management, modeled after Adobe Photoshop's History panel.

### Overview

The system maintains two types of state records:
- **History Entries**: Automatic, temporary records of each edit action
- **Snapshots**: User-created, persistent save points

### History Management

#### Automatic History Tracking
- **Auto-Generation**: Creates an entry after each document modification
- **Capacity**: Maintains maximum of 50 entries per editor module
- **FIFO Behavior**: Oldest entries are automatically removed when limit is reached
- **Session-Based**: History clears when document is closed (not persisted to disk)

#### History Navigation
- **Non-Destructive Selection**: Click any history entry to revert the editor state
- **Forward History**: Selecting an older entry deletes all subsequent history entries
- **Snapshot Preservation**: Snapshots created after the selected entry remain intact
- **Visual Feedback**: Active entry is highlighted with distinct styling

### Snapshot Management

#### Automatic Initial Snapshot
- **Snapshot 0**: Automatically created when a document initializes
- **Baseline State**: Provides a clean starting point for all edits

#### User-Created Snapshots
- **Manual Creation**: Use "New Snapshot" button (🕓) or context menu
- **Naming**: Auto-named sequentially (Snapshot 1, Snapshot 2, etc.)
- **Inline Rename**: Click snapshot name to rename
- **Persistence**: Saved to disk and survive across sessions

#### Snapshot Navigation
- **State Restoration**: Click snapshot to restore that exact state
- **History Reset**: Selecting a snapshot clears the entire current history stack
- **New History Chain**: After snapshot restoration, new edits create a fresh history sequence

### UI Components

#### Panel Layout
```
┌─────────────────────────────────┐
│ Toolbar [🕓] [🗑️] [⟳]          │
├─────────────────────────────────┤
│ 📷 Snapshot 3 (User Created)   │
│ 📷 Snapshot 2 (Initial State)  │
│ 📷 Snapshot 1 (Base)            │
│ 📷 Snapshot 0 (Automatic)       │
│ ─────────────────────────────── │
│ ▶ Paint Texture (Alpine)        │
│ ▶ Road Line Segment              │
│ ▶ Terrain Height Edit           │ ← Active Entry (highlighted)
│ ▶ Paint Texture (Desert)        │
└─────────────────────────────────┘
```

#### Toolbar Actions
- **New Snapshot (🕓)**: Create a new snapshot from current state
- **Delete Selected (🗑️)**: Remove selected snapshot or history entry
- **Revert (⟳)**: Revert to selected state

#### Context Menu
Right-click on any entry for additional options:
- **Revert**: Go back to this state
- **Rename**: Rename snapshot (snapshots only)
- **Delete**: Remove entry or snapshot

### Technical Details

#### Data Persistence
- **History Entries**: Stored in-memory only
  - Fast access and manipulation
  - Automatically cleared on document close
  - Not written to disk

- **Snapshots**: Persistent disk storage
  - Saved in project database
  - Loaded on project open
  - Survive application restarts

#### State Management
- **Document Projection**: Snapshots store complete document state via `SaveToProjection()`
- **Incremental Updates**: History entries may store delta changes for efficiency
- **Memory Management**: Old history entries automatically pruned to prevent memory growth

#### Command Integration
All editing commands implement the `ICommand` interface:
```csharp
public interface ICommand {
    void Execute();
    void Undo();
    string Description { get; }
}
```

Commands are automatically added to history on execution.

---

## Layers and Groups Management

The Layers and Groups Panel provides hierarchical organization of map content in the Landscape Editor.

### Overview

Layers act as containers for all types of map content (terrain, scenery, buildings, etc.) with support for hierarchical grouping and rendering control.

### Layer System

#### Layer Properties
- **Universal Containers**: A single layer can contain any mix of content types
- **No Type Sublayering**: All content types coexist on the same layer
- **Render Order**: Layer position in list determines rendering order (top = rendered last)
- **Base Layer**: Automatically created with every project, always bottom-most

#### Layer Operations

**Creating Layers**
- Use "New Layer" toolbar button or context menu
- New layers insert above currently selected layer
- Layers can be named and renamed at any time

**Deleting Layers**
- Select layer and use Delete button or context menu
- Confirmation prompt before deletion
- All content on layer is removed
- Base layer cannot be deleted

**Reordering Layers**
- Drag and drop to reorder
- Visual indicators show valid drop targets
- Affects rendering order in viewport
- Base layer cannot be reordered

### Group System

#### Hierarchical Organization
- **Unlimited Nesting**: Groups can contain layers and other groups
- **Collapsible**: Groups show expand/collapse arrows
- **Visual Hierarchy**: Nested items display with indentation
- **Cascade Operations**: Group deletion removes all nested content

#### Group Operations

**Creating Groups**
- Use "New Group" toolbar button or context menu
- Groups can be named and renamed

**Moving Items into Groups**
- Drag layers or groups onto target group
- Valid drop targets highlighted during drag
- Invalid drops prevented with visual feedback

**Deleting Groups**
- Confirmation prompt required
- All nested layers and groups are deleted
- Cannot be undone (unless snapshot exists)

### Visibility Controls

Each layer and group has two independent visibility toggles:

#### Visual Visibility (Eye Icon 👁️)
- **Purpose**: Controls viewport rendering
- **Behavior**: Hidden content not drawn in 3D view
- **Use Case**: Temporarily hide complex elements for easier editing
- **Inheritable**: Group visibility affects all nested items

#### Export Visibility (Box Icon ☐)
- **Purpose**: Controls export inclusion
- **Behavior**: Hidden content excluded from DAT file exports
- **Use Case**: Work-in-progress content that shouldn't be exported
- **Editing**: Content remains editable even when export-hidden

**Base Layer Visibility**
- Visibility toggles are visible but disabled (greyed out)
- Base layer always visible and always exported
- Cannot be changed by user

### UI Components

#### Panel Layout
```
┌─────────────────────────────────┐
│ [+ Layer] [+ Group] [Delete]    │
├─────────────────────────────────┤
│ 👁️ ☐ ▼ 📁 Buildings             │
│       👁️ ☐ Layer 3 (Houses)     │
│       👁️ ☐ ▶ 📁 Shops            │
│ 👁️ ☐ Layer 2 (Roads)            │
│ 👁️ ☐ Layer 1 (Terrain Details)  │
│ 👁️ ☐ 🔒 Base Layer               │ ← Always bottom, locked
└─────────────────────────────────┘
```

#### Visual Indicators
- **Selected Item**: Highlighted background color
- **Base Layer**: Lock icon (🔒) or distinct styling
- **Groups**: Folder icon (📁) with expand/collapse arrow
- **Layers**: Layer icon or no icon
- **Disabled Controls**: Greyed out buttons when action not available

#### Context Menu
Right-click on layers or groups:
- **Rename**: Change layer/group name
- **Delete**: Remove layer/group (with confirmation)
- **Create Group**: Create new group at current level

### Technical Details

#### Data Persistence
All layer/group configuration is saved in project files:
- Layer order and hierarchy
- Group nesting structure
- Layer and group names
- Visibility states (both types)

#### Naming Conventions
- Duplicate names are permitted
- No restrictions on name content
- Base layer name cannot be changed

#### Special Rules
**Base Layer Constraints:**
- Cannot be reordered
- Cannot be renamed
- Cannot be deleted
- Cannot be placed in groups
- Visibility toggles disabled

**Deletion Rules:**
- Confirmation required for all deletions
- Group deletion is cascading (removes all contents)
- No undo (user should create snapshot first)

---

## Terrain Editing

Advanced terrain modification tools for height map and landscape editing.

### Brush Tool

#### Features
- Variable brush size (radius in meters)
- Adjustable brush strength (0.0 to 1.0)
- Raise and lower terrain modes
- Smooth terrain option
- Real-time preview circle

#### Controls
- **Left Mouse**: Apply brush effect
- **Scroll Wheel**: Adjust brush size
- **Shift + Scroll**: Adjust brush strength
- **Ctrl**: Switch to lower mode
- **Alt**: Smooth mode

#### Technical Details
- Vertex-based height modification
- Normal recalculation for affected area
- GPU-accelerated terrain updates
- Command-based for undo/redo support

### Bucket Fill Tool

#### Features
- Fill entire height value regions
- Tolerance-based selection
- Preview before apply
- Works with current brush height

#### Use Cases
- Flatten large areas quickly
- Create plateaus
- Level building foundations
- Fill valleys or depressions

---

## Texture Painting

Multi-layer texture system for detailed terrain appearance.

### Texture Atlas System

#### Features
- Supports multiple terrain textures per landblock
- Automatic atlas packing and management
- Texture blending between layers
- UV coordinate generation

#### Texture Types
- Base terrain textures (grass, dirt, rock, etc.)
- Road textures
- Special terrain features
- Custom imported textures

### Painting Tools

#### Brush Sub-Tool
- Paint textures with circular brush
- Variable size and strength
- Blend modes for smooth transitions
- Layer-based painting

#### Bucket Fill Sub-Tool
- Fill regions with selected texture
- Tolerance-based selection
- Respects layer boundaries
- Fast for large areas

### Technical Implementation
- **Texture Coordinates**: Per-vertex UV mapping
- **Blending**: Multi-texture shader blending
- **Optimization**: Texture atlas reduces draw calls
- **Undo/Redo**: Full command support

---

## Road Drawing System

Comprehensive road creation and editing tools.

### Road Types

#### Supported Road Types (from AC)
- Dirt roads
- Paved roads
- Stone roads
- Wooden bridges
- Metal grates

### Drawing Tools

#### Line Sub-Tool
- Draw straight road segments
- Click to place start point
- Click again to place end point
- Automatic texture application
- Width control

#### Point Sub-Tool
- Place individual road control points
- Connect points to create roads
- Adjust point positions
- Control road curvature

#### Remove Sub-Tool
- Delete road segments
- Select and remove individual roads
- Undo support

### Road Properties
- **Width**: Configurable in meters
- **Type**: Selectable from available road types
- **Auto-Texturing**: Automatically applies appropriate texture
- **Collision**: Optional collision mesh generation

---

## Static Object Management

Place and manage static world objects (buildings, trees, rocks, etc.).

### Object Placement
- Select object from palette
- Click to place in world
- Automatic ground alignment
- Rotation controls

### Object Properties
- Position (X, Y, Z coordinates)
- Rotation (quaternion-based)
- Scale (uniform or per-axis)
- Model/resource reference

### Object Management
- Move, rotate, scale tools
- Copy and paste objects
- Delete selected objects
- Group selections

### Object Browser
- Searchable object palette
- Filter by category
- Preview thumbnails
- Recent objects list

---

## Project Management

Project-based workflow for organizing world data.

### Project Structure

#### Project Components
- **Project File**: SQLite database containing all project data
- **DAT References**: Links to Asheron's Call DAT files
- **Cache Directory**: Temporary files and extracted resources
- **Settings**: Per-project editor preferences

### Project Operations

#### Create New Project
1. Enter project name
2. Set project location
3. Configure DAT file paths
4. Initialize base terrain document

#### Open Existing Project
1. Browse to project file (.wbproj)
2. Load project settings
3. Restore last editor state
4. Open last active documents

#### Project Settings
- Project name and description
- DAT file paths
- Default terrain settings
- Editor preferences
- Recent files list

### Document System

#### Document Types
- **TerrainDocument**: Height and texture data for entire world
- **LandblockDocument**: Per-landblock data and objects (future)
- **Custom Documents**: Extensible for new content types

#### Document Lifecycle
1. **Load**: Document loaded from database on demand
2. **Edit**: Changes tracked in memory with command history
3. **Auto-Save**: Batched updates written to database every 2 seconds
4. **Unload**: Document saved and removed from memory when closed

### Export System

#### Export DAT Files
- Export modified terrain to DAT format
- Selective export based on layer visibility
- Backup original DATs automatically
- Validation before export

#### Export Formats (Future)
- JSON format for external tools
- OBJ/FBX for 3D modeling software
- Heightmap images (PNG/RAW)

---

## Keyboard Shortcuts

### Global
- `Ctrl+Z`: Undo
- `Ctrl+Y` / `Ctrl+Shift+Z`: Redo
- `Ctrl+S`: Save project (manual)
- `Ctrl+N`: New project
- `Ctrl+O`: Open project

### Viewport Navigation
- `W`: Move camera forward
- `S`: Move camera backward
- `A`: Move camera left
- `D`: Move camera right
- `Q`: Move camera down
- `E`: Move camera up
- `Middle Mouse + Drag`: Rotate camera
- `Scroll Wheel`: Zoom in/out

### Tools
- `B`: Brush tool
- `F`: Bucket fill tool
- `R`: Road drawing tool
- `O`: Object placement tool
- `Shift + Tool`: Invert tool action
- `Ctrl + Tool`: Alternative tool mode

### Layers
- `Ctrl+Click`: Multi-select layers
- `Delete`: Delete selected layers/groups
- `F2`: Rename selected layer/group

---

## Future Features

### Planned for v2.0
- **Multi-User Collaboration**: Real-time collaborative editing
- **Dungeon Editor**: Create and edit indoor spaces
- **Encounter Editor**: Place and configure monster spawns
- **Quest Editor**: Visual quest design tools
- **Lighting Editor**: Advanced lighting and atmosphere controls

### Under Consideration
- **Scripting API**: Automate repetitive tasks
- **Plugin System**: Extend functionality with custom tools
- **Asset Import**: Import custom 3D models and textures
- **Performance Profiling**: Optimize world for client performance
- **Version Control Integration**: Git-style branching and merging

---

## See Also

- [README.md](../README.md) - Project overview and setup
- [ARCHITECTURE.md](ARCHITECTURE.md) - Technical architecture details
- [API.md](API.md) - API reference documentation
- [GitHub Wiki](https://github.com/Chorizite/WorldBuilder/wiki) - Online documentation
