using System.Numerics;
using Xunit;

namespace Chorizite.OpenGLSDLBackend.Tests;

public class Camera3DTests {
    [Fact]
    public void Constructor_SetsInitialPosition() {
        var position = new Vector3(5, 10, 15);
        var camera = new Camera3D(position);

        Assert.Equal(position, camera.Position);
    }

    [Fact]
    public void Constructor_DefaultPosition_IsZero() {
        var camera = new Camera3D();

        Assert.Equal(Vector3.Zero, camera.Position);
    }

    [Fact]
    public void FieldOfView_DefaultValue() {
        var camera = new Camera3D();

        Assert.Equal(60.0f, camera.FieldOfView);
    }

    [Fact]
    public void FieldOfView_CanBeSet() {
        var camera = new Camera3D();

        camera.FieldOfView = 90.0f;

        Assert.Equal(90.0f, camera.FieldOfView);
    }

    [Fact]
    public void FieldOfView_ClampedToValidRange() {
        var camera = new Camera3D();

        camera.FieldOfView = 200.0f;
        Assert.True(camera.FieldOfView <= 179.0f);

        camera.FieldOfView = 0.0f;
        Assert.True(camera.FieldOfView >= 1.0f);
    }

    [Fact]
    public void Forward_ReturnsUnitVector() {
        var camera = new Camera3D();

        var forward = camera.Forward;
        float length = forward.Length();

        Assert.True(Math.Abs(length - 1.0f) < 0.001f);
    }

    [Fact]
    public void Right_ReturnsUnitVector() {
        var camera = new Camera3D();

        var right = camera.Right;
        float length = right.Length();

        Assert.True(Math.Abs(length - 1.0f) < 0.001f);
    }

    [Fact]
    public void Forward_IsYPositive_AtZeroAngles() {
        var camera = new Camera3D(Vector3.Zero, 0, 0);
        var forward = camera.Forward;

        // Z-up: Yaw 0 should point North (+Y)
        Assert.True(Math.Abs(forward.X) < 0.001f);
        Assert.True(Math.Abs(forward.Y - 1.0f) < 0.001f);
        Assert.True(Math.Abs(forward.Z) < 0.001f);
    }

    [Fact]
    public void Forward_IsZPositive_AtPitch90() {
        var camera = new Camera3D(Vector3.Zero, 0, 90);
        var forward = camera.Forward;

        // Z-up: Pitch 90 should point Up (+Z)
        Assert.True(Math.Abs(forward.X) < 0.001f);
        Assert.True(Math.Abs(forward.Y) < 0.001f);
        Assert.True(Math.Abs(forward.Z - 1.0f) < 0.001f);
    }

    [Fact]
    public void Right_IsXPositive_AtZeroAngles() {
        var camera = new Camera3D(Vector3.Zero, 0, 0);
        var right = camera.Right;

        // Z-up: Forward is +Y, Up is +Z -> Right = Cross(Forward, Up) = (+1, 0, 0)
        Assert.True(Math.Abs(right.X - 1.0f) < 0.001f);
        Assert.True(Math.Abs(right.Y) < 0.001f);
        Assert.True(Math.Abs(right.Z) < 0.001f);
    }

    [Fact]
    public void Forward_PerpendicularToRight() {
        var camera = new Camera3D();

        var forward = camera.Forward;
        var right = camera.Right;
        float dot = Vector3.Dot(forward, right);

        Assert.True(Math.Abs(dot) < 0.001f);
    }

    [Fact]
    public void ViewMatrix_IsNotIdentity_AfterResize() {
        var camera = new Camera3D();
        camera.Resize(800, 600);

        var view = camera.ViewMatrix;

        Assert.NotEqual(Matrix4x4.Identity, view);
    }

    [Fact]
    public void ProjectionMatrix_IsPerspective() {
        var camera = new Camera3D();
        camera.Resize(800, 600);

        var proj = camera.ProjectionMatrix;

        // Perspective matrices have M34 = -1 or 1 (depending on convention)
        Assert.True(Math.Abs(proj.M34) > 0);
    }

    [Fact]
    public void HandleKeyDown_W_EnablesForwardMovement() {
        var camera = new Camera3D();
        camera.Resize(800, 600);
        var initialPos = camera.Position;

        camera.HandleKeyDown("W");
        camera.Update(1.0f); // 1 second update

        Assert.NotEqual(initialPos, camera.Position);
    }

    [Fact]
    public void HandleKeyUp_W_StopsForwardMovement() {
        var camera = new Camera3D();
        camera.Resize(800, 600);

        camera.HandleKeyDown("W");
        camera.HandleKeyUp("W");
        var posAfterKeyUp = camera.Position;

        camera.Update(1.0f);

        Assert.Equal(posAfterKeyUp, camera.Position);
    }

    [Fact]
    public void HandleKeyDown_S_EnablesBackwardMovement() {
        var camera = new Camera3D();
        camera.Resize(800, 600);
        var initialPos = camera.Position;

        camera.HandleKeyDown("S");
        camera.Update(1.0f);

        Assert.NotEqual(initialPos, camera.Position);
    }

    [Fact]
    public void HandleKeyDown_A_EnablesLeftMovement() {
        var camera = new Camera3D();
        camera.Resize(800, 600);
        var initialPos = camera.Position;

        camera.HandleKeyDown("A");
        camera.Update(1.0f);

        Assert.NotEqual(initialPos, camera.Position);
    }

    [Fact]
    public void HandleKeyDown_D_EnablesRightMovement() {
        var camera = new Camera3D();
        camera.Resize(800, 600);
        var initialPos = camera.Position;

        camera.HandleKeyDown("D");
        camera.Update(1.0f);

        Assert.NotEqual(initialPos, camera.Position);
    }

    [Fact]
    public void HandlePointerMoved_WithLooking_ChangesViewMatrix() {
        var camera = new Camera3D();
        camera.Resize(800, 600);
        var initialView = camera.ViewMatrix;

        // Start looking (right click)
        camera.HandlePointerPressed(1, new Vector2(400, 300));
        // Move mouse
        camera.HandlePointerMoved(new Vector2(450, 320), new Vector2(50, 20));

        Assert.NotEqual(initialView, camera.ViewMatrix);
    }

    [Fact]
    public void HandlePointerMoved_WithoutLooking_DoesNotChangeViewMatrix() {
        var camera = new Camera3D();
        camera.Resize(800, 600);
        var initialView = camera.ViewMatrix;

        // Move mouse without pressing right click
        camera.HandlePointerMoved(new Vector2(450, 320), new Vector2(50, 20));

        Assert.Equal(initialView, camera.ViewMatrix);
    }

    [Fact]
    public void Resize_UpdatesProjectionMatrix() {
        var camera = new Camera3D();
        camera.Resize(400, 300); // 4:3 aspect ratio
        var proj1 = camera.ProjectionMatrix;

        camera.Resize(800, 400); // 2:1 aspect ratio (different)
        var proj2 = camera.ProjectionMatrix;

        // Different aspect ratios produce different projections
        Assert.NotEqual(proj1, proj2);
    }

    [Fact]
    public void MoveSpeed_CanBeSet() {
        var camera = new Camera3D();

        camera.MoveSpeed = 20.0f;

        Assert.Equal(20.0f, camera.MoveSpeed);
    }

    [Fact]
    public void LookSensitivity_CanBeSet() {
        var camera = new Camera3D();

        camera.LookSensitivity = 0.5f;

        Assert.Equal(0.5f, camera.LookSensitivity);
    }

    [Fact]
    public void HandlePointerWheelChanged_UpdatesMoveSpeed() {
        var camera = new Camera3D();
        var initialSpeed = camera.MoveSpeed;

        // Positive delta (scroll up) should increase speed
        camera.HandlePointerWheelChanged(1.0f);
        Assert.True(camera.MoveSpeed > initialSpeed);

        // Negative delta (scroll down) should decrease speed
        var fasterSpeed = camera.MoveSpeed;
        camera.HandlePointerWheelChanged(-1.0f);
        Assert.True(camera.MoveSpeed < fasterSpeed);
    }

    [Fact]
    public void FarPlane_CanBeSet() {
        var camera = new Camera3D();

        camera.FarPlane = 5000.0f;

        Assert.Equal(5000.0f, camera.FarPlane);
    }

    [Fact]
    public void FarPlane_UpdatesProjectionMatrix() {
        var camera = new Camera3D();
        camera.Resize(800, 600);
        var proj1 = camera.ProjectionMatrix;

        camera.FarPlane = 5000.0f;
        var proj2 = camera.ProjectionMatrix;

        Assert.NotEqual(proj1, proj2);
    }
}
