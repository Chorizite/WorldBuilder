using System;
using System.Numerics;
using Chorizite.Core.Render;
using Microsoft.Extensions.Logging;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace Chorizite.OpenGLSDLBackend.Lib;

public class CameraController {
    private readonly ILogger _log;
    private readonly Camera2D _camera2D;
    private readonly Camera3D _camera3D;
    private ICamera _currentCamera;
    private bool _is3DMode;

    public Camera2D Camera2D => _camera2D;
    public Camera3D Camera3D => _camera3D;
    public ICamera CurrentCamera => _currentCamera;
    public bool Is3DMode => _is3DMode;

    public event Action<bool>? OnCameraChanged;
    public event Action<float>? OnMoveSpeedChanged;

    public CameraController(ILogger log) {
        _log = log;
        _camera2D = new Camera2D(new Vector3(0, 0, 0));
        _camera3D = new Camera3D(new Vector3(0, -5, 2), 0, -22);
        _camera3D.OnMoveSpeedChanged += (speed) => OnMoveSpeedChanged?.Invoke(speed);
        _currentCamera = _camera3D;
        _is3DMode = true;
    }

    public void Teleport(Vector3 position, uint? cellId, EnvCellRenderManager? envCellManager, ref uint currentEnvCellId) {
        _currentCamera.Position = position;
        if (cellId.HasValue) {
            if ((cellId.Value & 0xFFFF) >= 0x0100) {
                currentEnvCellId = cellId.Value;
            }
            else {
                currentEnvCellId = 0;
            }
        }
        else {
            currentEnvCellId = envCellManager?.GetEnvCellAt(position, false) ?? 0;
        }
        _log.LogInformation("Teleported to {Position} in cell {CellId:X8}", position, currentEnvCellId);
    }

    public void ToggleCamera() {
        SyncCameraZ();
        _is3DMode = !_is3DMode;
        _currentCamera = _is3DMode ? _camera3D : _camera2D;
        _log.LogInformation("Camera toggled to {Mode} mode", _is3DMode ? "3D" : "2D");
        OnCameraChanged?.Invoke(_is3DMode);
    }

    public void SetCameraMode(bool is3d) {
        if (_is3DMode == is3d) return;

        SyncCameraZ();
        _is3DMode = is3d;
        _currentCamera = _is3DMode ? _camera3D : _camera2D;
        _log.LogInformation("Camera set to {Mode} mode", _is3DMode ? "3D" : "2D");
        OnCameraChanged?.Invoke(_is3DMode);
    }

    private void SyncCameraZ() {
        var fovRad = MathF.PI * _camera3D.FieldOfView / 180.0f;
        var tanHalfFov = MathF.Tan(fovRad / 2.0f);

        if (_is3DMode) {
            float h = Math.Max(0.01f, _camera3D.Position.Z);
            _camera2D.Zoom = 10.0f / (h * tanHalfFov);
            _camera2D.Position = _camera3D.Position;
        }
        else {
            float zoom = _camera2D.Zoom;
            float h = 10.0f / (zoom * tanHalfFov);
            _camera2D.Position = new Vector3(_camera2D.Position.X, _camera2D.Position.Y, h);
            _camera3D.Position = _camera2D.Position;
        }
    }

    public void SyncZoomFromZ() {
        var fovRad = MathF.PI * _camera3D.FieldOfView / 180.0f;
        var tanHalfFov = MathF.Tan(fovRad / 2.0f);
        float h = Math.Max(0.01f, _currentCamera.Position.Z);
        _camera2D.Zoom = 10.0f / (h * tanHalfFov);
    }

    public void Resize(int width, int height) {
        _camera2D.Resize(width, height);
        _camera3D.Resize(width, height);
    }

    public void Update(float deltaTime, EditorState state, ref uint currentEnvCellId, 
        TerrainRenderManager? terrainManager, StaticObjectRenderManager? staticObjectManager, 
        EnvCellRenderManager? envCellManager, PortalRenderManager? portalManager) {
        
        Vector3 oldPos = _currentCamera.Position;
        _currentCamera.Update(deltaTime);
        Vector3 newPos = _currentCamera.Position;
        Vector3 moveDir = newPos - oldPos;
        float moveDist = moveDir.Length();

        if (_is3DMode) {
            if (state.EnableCameraCollision) {
                if (moveDist > 0.0001f) {
                    Vector3 normalizedDir = Vector3.Normalize(moveDir);
                    SceneRaycastHit hit = SceneRaycastHit.NoHit;
                    bool hasHit = false;

                    if (currentEnvCellId != 0) {
                        if (envCellManager != null && envCellManager.Raycast(oldPos, normalizedDir, true, true, out hit, currentEnvCellId, true, moveDist + 0.5f)) {
                            if (hit.Distance <= moveDist + 0.5f) {
                                hasHit = true;
                            }
                        }
                    }
                    else {
                        if (staticObjectManager != null && staticObjectManager.Raycast(oldPos, normalizedDir, StaticObjectRenderManager.RaycastTarget.Buildings | StaticObjectRenderManager.RaycastTarget.StaticObjects, out hit, currentEnvCellId, true, moveDist + 0.5f)) {
                            if (hit.Distance <= moveDist + 0.5f) {
                                hasHit = true;
                            }
                        }
                    }

                    if (hasHit) {
                        newPos = oldPos + normalizedDir * Math.Max(0, hit.Distance - 0.5f);
                        _currentCamera.Position = newPos;
                        moveDist = (newPos - oldPos).Length(); // update moveDist after collision adjustment
                    }
                }
            }

            // Update current cell ID based on portal transition rules
            if (state.EnableCameraCollision) {
                if (moveDist > 0.0001f) {
                    if (portalManager != null && portalManager.Raycast(oldPos, moveDir / moveDist, out var portalHit, moveDist, true)) {
                        if (currentEnvCellId == 0) {
                            currentEnvCellId = portalHit.ObjectId;
                        }
                        else {
                            var nextCell = envCellManager?.GetEnvCellAt(newPos, false) ?? 0;
                            if (nextCell == currentEnvCellId && portalHit.ObjectId == currentEnvCellId) {
                                currentEnvCellId = 0;
                            }
                            else {
                                currentEnvCellId = nextCell;
                            }
                        }
                    }
                    else if (currentEnvCellId != 0) {
                        if ((envCellManager?.GetEnvCellAt(newPos, false) ?? 0) == 0) {
                            currentEnvCellId = 0;
                        }
                    }
                }

                // If we are at 0, we might have just loaded into a cell or teleported.
                // We check this if we moved or if the environment manager just loaded new data.
                if (currentEnvCellId == 0 && (moveDist > 0.0001f || (envCellManager?.NeedsPrepare ?? false) || (portalManager?.NeedsPrepare ?? false))) {
                    currentEnvCellId = envCellManager?.GetEnvCellAt(newPos, false) ?? 0;
                }
            }
            else {
                // When collision is off, always track the cell we are in if moving or data changed
                if (moveDist > 0.0001f || (envCellManager?.NeedsPrepare ?? false) || (portalManager?.NeedsPrepare ?? false)) {
                    currentEnvCellId = envCellManager?.GetEnvCellAt(newPos, false) ?? 0;
                }
            }

            if (currentEnvCellId == 0 && state.EnableCameraCollision && terrainManager != null) {
                var terrainHeight = terrainManager.GetHeight(newPos.X, newPos.Y);
                if (newPos.Z < terrainHeight + .6f) {
                    newPos.Z = terrainHeight + .6f;
                    _currentCamera.Position = newPos;
                }
            }
        }
        else {
            currentEnvCellId = envCellManager?.GetEnvCellAt(newPos, false) ?? 0;
        }
    }

    public void HandlePointerPressed(ViewportInputEvent e) {
        int button = e.IsLeftDown ? 0 : e.IsRightDown ? 1 : 2;
        _currentCamera.HandlePointerPressed(button, e.Position);
    }

    public void HandlePointerReleased(ViewportInputEvent e) {
        int button = e.ReleasedButton ?? (e.IsLeftDown ? 0 : e.IsRightDown ? 1 : 2);
        _currentCamera.HandlePointerReleased(button, e.Position);
    }

    public void HandlePointerMoved(ViewportInputEvent e) {
        _currentCamera.HandlePointerMoved(e.Position, e.Delta);
    }

    public void HandlePointerWheelChanged(float delta) {
        _currentCamera.HandlePointerWheelChanged(delta);
        if (!_is3DMode) {
            SyncCameraZ();
        }
    }

    public void HandleKeyDown(string key) {
        if (key.Equals("Tab", StringComparison.OrdinalIgnoreCase)) {
            ToggleCamera();
            return;
        }
        _currentCamera.HandleKeyDown(key);
    }

    public void HandleKeyUp(string key) {
        _currentCamera.HandleKeyUp(key);
    }
}