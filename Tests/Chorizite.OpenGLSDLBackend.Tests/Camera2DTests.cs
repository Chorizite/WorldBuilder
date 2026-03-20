using System.Numerics;
using Xunit;

namespace Chorizite.OpenGLSDLBackend.Tests;

public class Camera2DTests {
    [Fact]
    public void Constructor_SetsInitialPosition() {
        var position = new Vector3(10, 20, 0);
        var camera = new Camera2D(position);

        Assert.Equal(position, camera.Position);
    }

    [Fact]
    public void Constructor_DefaultPosition_IsZero() {
        var camera = new Camera2D();

        Assert.Equal(Vector3.Zero, camera.Position);
    }

    [Fact]
    public void Zoom_DefaultValue_IsOne() {
        var camera = new Camera2D();

        Assert.Equal(1.0f, camera.Zoom);
    }

    [Fact]
    public void Zoom_CanBeSet() {
        var camera = new Camera2D();

        camera.Zoom = 2.0f;

        Assert.Equal(2.0f, camera.Zoom);
    }

    [Fact]
    public void Zoom_ClampedToMinimum() {
        var camera = new Camera2D();
        camera.MinZoom = 0.5f;

        camera.Zoom = 0.1f;

        Assert.Equal(0.5f, camera.Zoom);
    }

    [Fact]
    public void Zoom_ClampedToMaximum() {
        var camera = new Camera2D();
        camera.MaxZoom = 10.0f;

        camera.Zoom = 100.0f;

        Assert.Equal(10.0f, camera.Zoom);
    }

    [Fact]
    public void HandlePointerWheelChanged_PositiveDelta_ZoomsIn() {
        var camera = new Camera2D();
        float initialZoom = camera.Zoom;

        camera.HandlePointerWheelChanged(1.0f);

        Assert.True(camera.Zoom > initialZoom);
    }

    [Fact]
    public void HandlePointerWheelChanged_NegativeDelta_ZoomsOut() {
        var camera = new Camera2D();
        camera.Zoom = 2.0f;
        float initialZoom = camera.Zoom;

        camera.HandlePointerWheelChanged(-1.0f);

        Assert.True(camera.Zoom < initialZoom);
    }

    [Fact]
    public void ViewMatrix_IsNotIdentity_AfterResize() {
        var camera = new Camera2D(new Vector3(100, 100, 0));
        camera.Resize(800, 600);

        var view = camera.ViewMatrix;

        Assert.NotEqual(Matrix4x4.Identity, view);
    }

    [Fact]
    public void ProjectionMatrix_IsOrthographic() {
        var camera = new Camera2D();
        camera.Resize(800, 600);

        var proj = camera.ProjectionMatrix;

        // Orthographic matrices have specific characteristics
        // The perspective divide row should be (0, 0, 0, 1)
        Assert.Equal(0, proj.M14);
        Assert.Equal(0, proj.M24);
        Assert.Equal(0, proj.M34);
        Assert.Equal(1, proj.M44);
    }

    [Fact]
    public void Resize_UpdatesProjectionMatrix() {
        var camera = new Camera2D();
        camera.Resize(400, 300); // 4:3 aspect ratio
        var proj1 = camera.ProjectionMatrix;

        camera.Resize(800, 400); // 2:1 aspect ratio (different)
        var proj2 = camera.ProjectionMatrix;

        Assert.NotEqual(proj1, proj2);
    }

    [Fact]
    public void HandlePointerMoved_WithPanning_MovesCamera() {
        var camera = new Camera2D();
        camera.Resize(800, 600);
        var initialPos = camera.Position;

        // Start panning (right click)
        camera.HandlePointerPressed(1, new Vector2(100, 100));
        // Move pointer
        camera.HandlePointerMoved(new Vector2(150, 150), new Vector2(50, 50));

        Assert.NotEqual(initialPos, camera.Position);
    }

    [Fact]
    public void HandlePointerMoved_WithoutPanning_DoesNotMoveCamera() {
        var camera = new Camera2D();
        camera.Resize(800, 600);
        var initialPos = camera.Position;

        // Move pointer without pressing right click
        camera.HandlePointerMoved(new Vector2(150, 150), new Vector2(50, 50));

        Assert.Equal(initialPos, camera.Position);
    }

    [Fact]
    public void HandlePointerReleased_StopsPanning() {
        var camera = new Camera2D();
        camera.Resize(800, 600);

        // Start and stop panning
        camera.HandlePointerPressed(1, new Vector2(100, 100));
        camera.HandlePointerReleased(1, new Vector2(100, 100));

        var initialPos = camera.Position;
        camera.HandlePointerMoved(new Vector2(200, 200), new Vector2(100, 100));

        Assert.Equal(initialPos, camera.Position);
    }
}