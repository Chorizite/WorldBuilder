using Microsoft.Extensions.Logging;
using Moq;
using Silk.NET.OpenGL;
using System.Numerics;
using WorldBuilder.Shared.Models;
using Xunit;

namespace Chorizite.OpenGLSDLBackend.Tests;

public class GameSceneTests {
    private readonly GameScene _gameScene;

    public GameSceneTests() {
        var mockGl = new Mock<GL>(MockBehavior.Loose, new object[] { null! });
        var mockGraphicsDevice = new Mock<OpenGLGraphicsDevice>(MockBehavior.Loose, new object[] { null!, null! });
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        var mockLogger = new Mock<ILogger>();
        mockLoggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(mockLogger.Object);

        _gameScene = new GameScene(mockGl.Object, mockGraphicsDevice.Object, mockLoggerFactory.Object);
    }

    [Fact]
    public void Constructor_StartsIn3DMode() {
        Assert.True(_gameScene.Is3DMode);
        Assert.IsType<Camera3D>(_gameScene.CurrentCamera);
    }

    [Fact]
    public void ToggleCamera_SwitchesBetweenModes() {
        _gameScene.ToggleCamera();
        Assert.False(_gameScene.Is3DMode);
        Assert.IsType<Camera2D>(_gameScene.CurrentCamera);

        _gameScene.ToggleCamera();
        Assert.True(_gameScene.Is3DMode);
        Assert.IsType<Camera3D>(_gameScene.CurrentCamera);
    }

    [Fact]
    public void HandleKeyDown_Tab_TogglesCamera() {
        Assert.True(_gameScene.Is3DMode);
        _gameScene.HandleKeyDown("Tab");
        Assert.False(_gameScene.Is3DMode);
    }

    [Fact]
    public void HandlePointerWheelChanged_In2DMode_ZoomsCamera() {
        _gameScene.ToggleCamera(); // Switch to 2D
        var camera2D = (Camera2D)_gameScene.CurrentCamera;
        float initialZoom = camera2D.Zoom;

        _gameScene.HandlePointerWheelChanged(1.0f);

        Assert.NotEqual(initialZoom, camera2D.Zoom);
    }

    [Fact]
    public void HandlePointerMoved_In2DMode_Panning_MovesCamera() {
        _gameScene.ToggleCamera(); // Switch to 2D
        var camera2D = (Camera2D)_gameScene.CurrentCamera;
        var initialPos = camera2D.Position;

        // Right click (button 1) to start panning
        _gameScene.HandlePointerPressed(new ViewportInputEvent {
            IsRightDown = true,
            Position = new Vector2(100, 100)
        });

        _gameScene.HandlePointerMoved(new ViewportInputEvent {
            IsRightDown = true,
            Position = new Vector2(110, 110),
            Delta = new Vector2(10, 10)
        });

        Assert.NotEqual(initialPos, camera2D.Position);
    }

    [Fact]
    public void HandlePointerMoved_In3DMode_Looking_ChangesView() {
        // Starts in 3D mode
        var camera3D = (Camera3D)_gameScene.CurrentCamera;
        camera3D.Resize(800, 600);
        var initialView = camera3D.ViewMatrix;

        // Right click (button 1) to start looking
        _gameScene.HandlePointerPressed(new ViewportInputEvent {
            IsRightDown = true,
            Position = new Vector2(400, 300)
        });

        _gameScene.HandlePointerMoved(new ViewportInputEvent {
            IsRightDown = true,
            Position = new Vector2(410, 310),
            Delta = new Vector2(10, 10)
        });

        Assert.NotEqual(initialView, camera3D.ViewMatrix);
    }

    [Fact]
    public void HandleKeyDown_In3DMode_MovesCamera() {
        // Starts in 3D mode
        var camera3D = (Camera3D)_gameScene.CurrentCamera;
        camera3D.Resize(800, 600);
        var initialPos = camera3D.Position;

        _gameScene.HandleKeyDown("W");
        _gameScene.Update(0.1f);

        Assert.NotEqual(initialPos, camera3D.Position);
    }

    [Fact]
    public void ToggleCamera_SyncsZLevel() {
        // Starts in 3D mode at Z=2 (default)
        var camera3D = (Camera3D)_gameScene.CurrentCamera;
        float height = camera3D.Position.Z;

        _gameScene.ToggleCamera(); // Switch to 2D
        var camera2D = (Camera2D)_gameScene.CurrentCamera;

        // Check if Zoom is set based on height
        float fovRad = MathF.PI * camera3D.FieldOfView / 180.0f;
        float expectedZoom = 10.0f / (height * MathF.Tan(fovRad / 2.0f));
        Assert.Equal(expectedZoom, camera2D.Zoom, 4);

        // Change Zoom and switch back
        camera2D.Zoom = 2.0f;
        _gameScene.ToggleCamera(); // Switch to 3D

        float expectedHeight = 10.0f / (2.0f * MathF.Tan(fovRad / 2.0f));
        Assert.Equal(expectedHeight, _gameScene.CurrentCamera.Position.Z, 4);
    }

    [Fact]
    public void EnableTransparencyPass_DefaultsToTrue() {
        Assert.True(_gameScene.EnableTransparencyPass);
    }

    [Fact]
    public void ShowDebugShapes_DefaultsToTrue() {
        Assert.True(_gameScene.ShowDebugShapes);
    }

    [Fact]
    public void ShowDebugShapes_CanBeToggled() {
        _gameScene.ShowDebugShapes = false;
        Assert.False(_gameScene.ShowDebugShapes);
        _gameScene.ShowDebugShapes = true;
        Assert.True(_gameScene.ShowDebugShapes);
    }
}