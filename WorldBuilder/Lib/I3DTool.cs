using Avalonia.Input;
using Chorizite.Core.Render;
using Chorizite.OpenGLSDLBackend;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Tools.Landscape;

namespace WorldBuilder.Lib {
    public interface I3DTool {
        int Width { get; set; }
        int Height { get; set; }
        RenderTarget RenderTarget { get; }
        TerrainEditingContext _editingContext { get; }
        CameraManager _cameraManager { get; }
        TerrainProvider _terrainGenerator { get; }

        void Init(Project project, OpenGLRenderer renderer);
        void Update(double frameTime, AvaloniaInputState inputState);
        void Render();
        bool HandleMouseMove(PointerEventArgs e, object editingContext, MouseState mouseState);
        void HandleMouseDown(MouseState mouseState, object editingContext);
        void HandleMouseUp(MouseState mouseState, object editingContext);
        void OnMouseScroll(PointerWheelEventArgs e, Vector2 position);
    }
}