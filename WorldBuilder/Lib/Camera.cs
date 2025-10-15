using System;
using System.Numerics;
using WorldBuilder.Lib.Settings;

namespace WorldBuilder.Lib {
    public interface ICamera {
        Vector3 Position { get; }
        Vector3 Front { get; }
        Vector3 Up { get; }
        Vector3 Right { get; }

        Vector2 ScreenSize { get; set; }

        Matrix4x4 GetViewMatrix();
        Matrix4x4 GetProjectionMatrix();

        void ProcessKeyboard(CameraMovement direction, double deltaTime);
        void ProcessMouseMovement(MouseState mouseState);
        void ProcessMouseScroll(float yOffset);

        void SetMovementSpeed(float speed);
        void SetMouseSensitivity(float sensitivity);
        void SetPosition(Vector3 newPosition);
        void SetPosition(float x, float y, float z);
        void LookAt(Vector3 target);
    }

    public class PerspectiveCamera : ICamera {
        private Vector3 position;
        private Vector3 front;
        private Vector3 up;
        private Vector3 right;
        private Vector3 worldUp;

        private float yaw;
        private float pitch;
        private WorldBuilderSettings settings;

        internal float movementSpeed {
            get => settings.Landscape.Camera.MovementSpeed;
            set => settings.Landscape.Camera.MovementSpeed = value;
        }

        internal float mouseSensitivity {
            get => settings.Landscape.Camera.MouseSensitivity / 10f;
            set => settings.Landscape.Camera.MouseSensitivity = value * 10f;
        }
        internal float fov {
            get => settings.Landscape.Camera.FieldOfView;
            set => settings.Landscape.Camera.FieldOfView = (int)value;
        }
        private bool _isDragging;
        private Vector2 _previousMousePosition;

        public Vector3 Position => position;
        public Vector3 Front => front;
        public Vector3 Up => up;
        public Vector3 Right => right;

        public Vector2 ScreenSize { get; set; }

        // Camera options
        private const float DefaultYaw = 0.0f;  // 0° points along +Y
        private const float DefaultPitch = -30.0f; // Look down at terrain by default

        public PerspectiveCamera(Vector3 position, WorldBuilderSettings settings) {
            this.position = position;
            this.worldUp = -Vector3.UnitZ;
            this.yaw = DefaultYaw;
            this.pitch = DefaultPitch;
            this.settings = settings;

            UpdateCameraVectors();
        }

        public Matrix4x4 GetViewMatrix() {
            return Matrix4x4.CreateLookAtLeftHanded(position, position + front, up);
        }

        public Matrix4x4 GetProjectionMatrix() {
            return Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(
                MathHelper.DegreesToRadians(fov),
                ScreenSize.X / ScreenSize.Y,
                0.1f,
                settings.Landscape.Camera.MaxDrawDistance);
        }

        public void ProcessKeyboard(CameraMovement direction, double deltaTime) {
            float velocity = movementSpeed * (float)deltaTime;
            switch (direction) {
                case CameraMovement.Forward:
                    position += front * velocity;
                    break;
                case CameraMovement.Backward:
                    position -= front * velocity;
                    break;
                case CameraMovement.Left:
                    position += right * velocity;
                    break;
                case CameraMovement.Right:
                    position -= right * velocity;
                    break;
                case CameraMovement.Up:
                    position -= worldUp * velocity; // Use worldUp instead of local up
                    break;
                case CameraMovement.Down:
                    position += worldUp * velocity; // Use worldUp instead of local up
                    break;
            }
        }

        public void ProcessMouseMovement(MouseState mouseState) {
            if (mouseState.RightPressed) {
                if (!_isDragging) {
                    _isDragging = true;
                    _previousMousePosition = mouseState.Position;
                }
                else {
                    // Calculate per-frame delta
                    var xOffset = (mouseState.Position.X - _previousMousePosition.X) * mouseSensitivity;
                    var yOffset = (mouseState.Position.Y - _previousMousePosition.Y) * mouseSensitivity;

                    // Update yaw and pitch (inverted for intuitive control)
                    yaw -= xOffset; // Dragging right decreases yaw (rotates left)
                    pitch -= yOffset; // Dragging up decreases pitch (tilts down)

                    // Constrain pitch to avoid flipping
                    pitch = Math.Clamp(pitch, -89.0f, 89.0f);

                    UpdateCameraVectors();

                    // Update previous position after processing movement
                    _previousMousePosition = mouseState.Position;
                }
            }
            else {
                _isDragging = false;
            }
        }

        public void ProcessMouseScroll(float yOffset) {
            movementSpeed += yOffset * 500f; // Adjust speed with scroll
            movementSpeed = Math.Max(12f, movementSpeed);
        }

        public void SetMovementSpeed(float speed) {
            movementSpeed = speed;
        }

        public void SetMouseSensitivity(float sensitivity) {
            mouseSensitivity = sensitivity;
        }

        public void SetPosition(Vector3 newPosition) {
            position = newPosition;
        }

        public void SetPosition(float x, float y, float z) {
            position = new Vector3(x, y, z);
        }

        private void UpdateCameraVectors() {
            Vector3 newFront;

            // Corrected vector calculations for Z-up coordinate system
            newFront.X = MathF.Cos(MathHelper.DegreesToRadians(yaw)) * MathF.Cos(MathHelper.DegreesToRadians(pitch));
            newFront.Y = MathF.Sin(MathHelper.DegreesToRadians(yaw)) * MathF.Cos(MathHelper.DegreesToRadians(pitch));
            newFront.Z = MathF.Sin(MathHelper.DegreesToRadians(pitch));

            front = Vector3.Normalize(newFront);

            // Calculate right and up vectors for Z-up system
            right = Vector3.Normalize(Vector3.Cross(front, worldUp));
            up = Vector3.Normalize(Vector3.Cross(right, front));
        }

        public void LookAt(Vector3 target) {
            Vector3 direction = Vector3.Normalize(target - position);

            // Calculate yaw and pitch from direction vector for Z-up system
            // Yaw: angle from +X axis in the XY plane
            yaw = MathHelper.RadiansToDegrees(MathF.Atan2(direction.Y, direction.X));

            // Pitch: angle from horizontal plane toward +Z
            float horizontalDistance = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            pitch = MathHelper.RadiansToDegrees(MathF.Atan2(direction.Z, horizontalDistance));

            // Update camera vectors with new angles
            UpdateCameraVectors();
        }
    }

    public class OrthographicTopDownCamera : ICamera {
        private WorldBuilderSettings settings;
        private Vector3 position;
        private Vector3 front;
        private Vector3 up;
        private Vector3 right;
        private Vector3 worldUp;

        private float movementSpeed {
            get { return settings.Landscape.Camera.MovementSpeed / 40f; }
            set { settings.Landscape.Camera.MovementSpeed = value * 40f; }
        }
        private float mouseSensitivity {
            get { return settings.Landscape.Camera.MouseSensitivity / 10f; }
            set { settings.Landscape.Camera.MouseSensitivity = value * 10f; }
        }
        private float orthographicSize = 1800f; // Size of the orthographic view
        private bool _isDragging;
        private Vector2 _previousMousePosition;

        public Vector3 Position => position;
        public Vector3 Front => front;
        public Vector3 Up => up;
        public Vector3 Right => right;
        public float OrthographicSize {
            get { return orthographicSize; }
            set { orthographicSize = value; }
        }

        public Vector2 ScreenSize { get; set; }

        private const float DefaultHeight = 1000.0f;

        public OrthographicTopDownCamera(Vector3 position, WorldBuilderSettings settings) {
            this.settings = settings;
            // Position the camera above the target point
            this.position = new Vector3(position.X, position.Y, position.Z + DefaultHeight);

            // For top-down view, camera looks straight down
            this.front = new Vector3(0, 0, -1);
            this.worldUp = new Vector3(0, 0, 0); // Y is up in world space for horizontal movement
            this.up = new Vector3(0, -1, 0);
            this.right = new Vector3(1, 0, 0);
        }

        public Matrix4x4 GetViewMatrix() {
            return Matrix4x4.CreateLookAtLeftHanded(position, position + front, up);
        }

        public Matrix4x4 GetProjectionMatrix() {
            float width = orthographicSize * (ScreenSize.X / ScreenSize.Y);
            float height = orthographicSize;

            return Matrix4x4.CreateOrthographicLeftHanded(
                width,
                height,
                0.1f,
                100000f);
        }

        public void ProcessKeyboard(CameraMovement direction, double deltaTime) {
            // Scale movement speed based on zoom level for consistent feel
            float scaledSpeed = movementSpeed * (float)deltaTime * (orthographicSize / 50.0f);

            switch (direction) {
                case CameraMovement.Forward:
                    // Move forward in XY plane (positive Y)
                    position += new Vector3(0, scaledSpeed, 0);
                    break;
                case CameraMovement.Backward:
                    // Move backward in XY plane (negative Y)
                    position -= new Vector3(0, scaledSpeed, 0);
                    break;
                case CameraMovement.Left:
                    // Move left in XY plane (negative X)
                    position -= new Vector3(scaledSpeed, 0, 0);
                    break;
                case CameraMovement.Right:
                    // Move right in XY plane (positive X)
                    position += new Vector3(scaledSpeed, 0, 0);
                    break;
                case CameraMovement.Up:
                    // Move up in Z (higher altitude)
                    position += new Vector3(0, 0, scaledSpeed);
                    break;
                case CameraMovement.Down:
                    // Move down in Z (lower altitude)
                    position -= new Vector3(0, 0, scaledSpeed);
                    break;
            }
        }

        public void ProcessMouseMovement(MouseState mouseState) {
            if (mouseState.RightPressed) {
                if (!_isDragging) {
                    _isDragging = true;
                    _previousMousePosition = mouseState.Position;
                }
                else {
                    // Calculate delta in screen space
                    Vector2 mouseDelta = mouseState.Position - _previousMousePosition;

                    // Convert delta to world space
                    float aspectRatio = ScreenSize.X / ScreenSize.Y;
                    float worldDeltaX = -mouseDelta.X * (orthographicSize * aspectRatio / ScreenSize.X);
                    float worldDeltaY = mouseDelta.Y * (orthographicSize / ScreenSize.Y);

                    // Update camera position
                    position += new Vector3(worldDeltaX, worldDeltaY, 0);

                    // Update previous position after processing movement
                    _previousMousePosition = mouseState.Position;
                }
            }
            else {
                _isDragging = false;
            }
        }

        public void ProcessMouseScroll(float yOffset) {
            float zoomSensitivity = orthographicSize * 0.1f; // 10% of current zoom level

            float oldSize = orthographicSize;
            orthographicSize -= yOffset * zoomSensitivity;
            orthographicSize = MathF.Max(1.0f, MathF.Min(orthographicSize, 100000.0f));
        }

        public void ProcessMouseScrollAtCursor(float yOffset, Vector2 mouseScreenPos, Vector2 screenSize) {
            float oldSize = orthographicSize;

            // Calculate zoom factor based on current orthographic size
            float zoomSensitivity = orthographicSize * 0.1f;

            orthographicSize -= yOffset * zoomSensitivity;
            orthographicSize = MathF.Max(1.0f, MathF.Min(orthographicSize, 100000.0f));

            // Calculate the world position under the mouse cursor before zoom
            Vector2 normalizedMousePos = new Vector2(
                (mouseScreenPos.X / screenSize.X - 0.5f) * 2.0f,
                (0.5f - mouseScreenPos.Y / screenSize.Y) * 2.0f // Flip Y
            );

            float aspectRatio = screenSize.X / screenSize.Y;
            Vector2 worldMousePos = new Vector2(
                position.X + normalizedMousePos.X * oldSize * aspectRatio * 0.5f,
                position.Y + normalizedMousePos.Y * oldSize * 0.5f
            );

            // Calculate the new world position under the mouse cursor after zoom
            Vector2 newWorldMousePos = new Vector2(
                position.X + normalizedMousePos.X * orthographicSize * aspectRatio * 0.5f,
                position.Y + normalizedMousePos.Y * orthographicSize * 0.5f
            );

            // Adjust camera position to keep the same world point under the cursor
            Vector2 offset = worldMousePos - newWorldMousePos;
            position += new Vector3(offset.X, offset.Y, 0);
        }

        public void SetMovementSpeed(float speed) {
            movementSpeed = Math.Max(12f, speed);
        }

        public void SetMouseSensitivity(float sensitivity) {
            mouseSensitivity = sensitivity;
        }

        public void SetPosition(Vector3 newPosition) {
            position = new Vector3(newPosition.X, newPosition.Y, newPosition.Z);
            orthographicSize = newPosition.Z;
        }

        public void SetPosition(float x, float y, float z) {
            position = new Vector3(x, y, z);
        }

        public void LookAt(Vector3 target) {
            position = new Vector3(target.X, target.Y, position.Z);
        }
    }

    public enum CameraMovement {
        Forward,
        Backward,
        Left,
        Right,
        Up,
        Down
    }

    public static class MathHelper {
        public static float DegreesToRadians(float degrees) {
            return degrees * (MathF.PI / 180.0f);
        }

        public static float RadiansToDegrees(float radians) {
            return radians * (180.0f / MathF.PI);
        }
    }

    public class CameraManager {
        private ICamera currentCamera;

        public ICamera Current => currentCamera;

        public CameraManager(ICamera initialCamera) {
            currentCamera = initialCamera;
        }

        public void SwitchCamera(ICamera newCamera) {
            newCamera.SetPosition(Current.Position);
            currentCamera = newCamera;
        }
    }
}