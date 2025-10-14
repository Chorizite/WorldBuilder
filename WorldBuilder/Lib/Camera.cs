using System;
using System.Numerics;
using WorldBuilder.Lib.Settings;

namespace WorldBuilder.Lib {
    public interface ICamera {
        Vector3 Position { get; }
        Vector3 Front { get; }
        Vector3 Up { get; }
        Vector3 Right { get; }

        Vector2 ScreenSize { get; set;  }

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

        // Animation state for smooth transitions
        private bool isAnimating;
        private float targetPitch;
        private float targetYaw;
        private float animationStartPitch;
        private float animationStartYaw;
        private float animationProgress; // 0 to 1

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
                // Up/Down removed - use mouse wheel for altitude control
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
            // Zoom in/out by adjusting altitude (Z position)
            // Use current altitude to scale zoom speed for smooth zooming at any height
            float currentAltitude = position.Z;
            float zoomSpeed = Math.Max(50f, currentAltitude * 0.1f); // At least 50 units, or 10% of altitude

            // Apply user-configurable sensitivity multiplier
            float sensitivity = settings.Landscape.Camera.MouseWheelZoomSensitivity;

            // Positive yOffset = scroll up = zoom IN (move down/closer)
            // Negative yOffset = scroll down = zoom OUT (move up/farther)
            // This matches orthographic behavior for consistency when switching modes
            position.Z -= yOffset * zoomSpeed * sensitivity;

            // Clamp to reasonable altitude range
            position.Z = Math.Max(10f, position.Z); // Minimum 10 units above ground
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

        /// <summary>
        /// Start animating the camera to top-down orientation (pitch and yaw)
        /// </summary>
        public void AnimateToTopDown() {
            isAnimating = true;
            animationStartPitch = pitch;
            animationStartYaw = yaw;
            targetPitch = -89.0f; // Almost straight down
            targetYaw = 0.0f; // Face north
            animationProgress = 0f;
        }

        /// <summary>
        /// Update animation state - call this every frame
        /// </summary>
        /// <param name="deltaTime">Time since last frame in seconds</param>
        /// <returns>True if animation is still in progress</returns>
        public bool UpdateAnimation(double deltaTime) {
            if (!isAnimating) return false;

            // Animation speed - takes 0.5 seconds to complete
            float animationSpeed = 2.0f;
            animationProgress += (float)deltaTime * animationSpeed;

            if (animationProgress >= 1.0f) {
                // Animation complete
                animationProgress = 1.0f;
                pitch = targetPitch;
                yaw = targetYaw;
                isAnimating = false;
                UpdateCameraVectors();
                return false;
            }

            // Smooth interpolation using ease-in-out
            float t = animationProgress;
            float smoothT = t * t * (3f - 2f * t); // Smoothstep function

            // Interpolate both pitch and yaw
            pitch = MathHelper.Lerp(animationStartPitch, targetPitch, smoothT);

            // Handle yaw wrapping (shortest path)
            float yawDiff = targetYaw - animationStartYaw;
            // Normalize to -180 to 180
            while (yawDiff > 180f) yawDiff -= 360f;
            while (yawDiff < -180f) yawDiff += 360f;
            yaw = animationStartYaw + yawDiff * smoothT;

            UpdateCameraVectors();
            return true;
        }

        /// <summary>
        /// Set the camera to a top-down orientation (looking straight down, facing north)
        /// </summary>
        public void SetTopDownOrientation() {
            pitch = -89.0f; // Almost straight down (can't be exactly -90 due to constraints)
            yaw = 0.0f; // Face north
            UpdateCameraVectors();
        }

        /// <summary>
        /// Set the camera to a top-down orientation with specific yaw
        /// </summary>
        public void SetTopDownOrientation(float specificYaw) {
            pitch = -89.0f; // Almost straight down (can't be exactly -90 due to constraints)
            yaw = specificYaw;
            UpdateCameraVectors();
        }

        /// <summary>
        /// Reset camera orientation to face north at horizontal level
        /// </summary>
        public void ResetOrientation() {
            yaw = 0.0f;     // Face north
            pitch = 0.0f;   // Look horizontally
            UpdateCameraVectors();
        }

        /// <summary>
        /// Check if the camera is currently animating
        /// </summary>
        public bool IsAnimating => isAnimating;

        /// <summary>
        /// Get the current yaw angle
        /// </summary>
        public float Yaw => yaw;
    }

    public class OrthographicTopDownCamera : ICamera {
        private WorldBuilderSettings settings;
        private Vector3 position;
        private Vector3 front;
        private Vector3 up;
        private Vector3 right;
        private Vector3 worldUp;
        private float yaw; // Rotation around Z axis (top-down rotation)

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
        private bool _isRotating;
        private Vector2 _previousMousePosition;
        private Vector2 _previousRotateMousePosition;

        public Vector3 Position => position;
        public Vector3 Front => front;
        public Vector3 Up => up;
        public Vector3 Right => right;
        public float OrthographicSize => orthographicSize;
        public float Yaw {
            get => yaw;
            set {
                yaw = value;
                UpdateCameraVectors();
            }
        }

        public Vector2 ScreenSize { get; set; }

        private const float DefaultHeight = 1000.0f;

        public OrthographicTopDownCamera(Vector3 position, WorldBuilderSettings settings, float initialYaw = 0.0f) {
            this.settings = settings;
            // Position the camera above the target point
            this.position = new Vector3(position.X, position.Y, position.Z + DefaultHeight);

            // For top-down view, camera looks straight down
            this.front = new Vector3(0, 0, -1);
            this.worldUp = new Vector3(0, 0, 1); // Z is up in world space
            this.yaw = initialYaw;

            UpdateCameraVectors();
        }

        private void UpdateCameraVectors() {
            // For top-down camera, we rotate the up and right vectors based on yaw
            // Front always points down (-Z)
            float yawRad = MathHelper.DegreesToRadians(yaw);

            // Calculate right vector by rotating around Z axis
            right = new Vector3(MathF.Cos(yawRad), MathF.Sin(yawRad), 0);
            right = Vector3.Normalize(right);

            // Calculate up vector (perpendicular to right in XY plane)
            up = new Vector3(-MathF.Sin(yawRad), MathF.Cos(yawRad), 0);
            up = Vector3.Normalize(up);
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
                    // Move forward relative to camera rotation (screen up / north)
                    position -= up * scaledSpeed;
                    break;
                case CameraMovement.Backward:
                    // Move backward relative to camera rotation (screen down / south)
                    position += up * scaledSpeed;
                    break;
                case CameraMovement.Left:
                    // Move left relative to camera rotation (screen left / west)
                    position += right * scaledSpeed;
                    break;
                case CameraMovement.Right:
                    // Move right relative to camera rotation (screen right / east)
                    position -= right * scaledSpeed;
                    break;
                case CameraMovement.Up:
                    // Zoom out (increase orthographic size)
                    float zoomOutSpeed = orthographicSize * 2.0f * (float)deltaTime;
                    orthographicSize += zoomOutSpeed;
                    orthographicSize = MathF.Min(orthographicSize, 100000.0f);
                    break;
                case CameraMovement.Down:
                    // Zoom in (decrease orthographic size)
                    float zoomInSpeed = orthographicSize * 2.0f * (float)deltaTime;
                    orthographicSize -= zoomInSpeed;
                    orthographicSize = MathF.Max(1.0f, orthographicSize);
                    break;
            }
        }

        public void ProcessMouseMovement(MouseState mouseState) {
            // Handle right-click panning
            if (mouseState.RightPressed) {
                if (!_isDragging) {
                    _isDragging = true;
                    _previousMousePosition = mouseState.Position;
                }
                else {
                    // Calculate delta in screen space
                    Vector2 mouseDelta = mouseState.Position - _previousMousePosition;

                    // Convert delta to world space, accounting for rotation
                    float aspectRatio = ScreenSize.X / ScreenSize.Y;
                    float screenDeltaX = mouseDelta.X * (orthographicSize * aspectRatio / ScreenSize.X);
                    float screenDeltaY = -mouseDelta.Y * (orthographicSize / ScreenSize.Y);

                    // Apply movement in camera-relative directions (inverted for natural panning)
                    position -= right * screenDeltaX + up * screenDeltaY;

                    // Update previous position after processing movement
                    _previousMousePosition = mouseState.Position;
                }
            }
            else {
                _isDragging = false;
            }

            // Handle middle-click rotation
            if (mouseState.MiddlePressed) {
                if (!_isRotating) {
                    _isRotating = true;
                    _previousRotateMousePosition = mouseState.Position;
                }
                else {
                    // Calculate horizontal mouse movement
                    float deltaX = mouseState.Position.X - _previousRotateMousePosition.X;

                    // Rotate based on horizontal movement
                    float rotationSpeed = 0.3f; // Adjust sensitivity
                    yaw -= deltaX * rotationSpeed;

                    // Normalize yaw to -180 to 180
                    while (yaw > 180f) yaw -= 360f;
                    while (yaw < -180f) yaw += 360f;

                    UpdateCameraVectors();

                    // Update previous position after processing rotation
                    _previousRotateMousePosition = mouseState.Position;
                }
            }
            else {
                _isRotating = false;
            }
        }

        public void ProcessMouseScroll(float yOffset) {
            float zoomSensitivity = orthographicSize * 0.1f; // 10% of current zoom level

            // Apply user-configurable sensitivity multiplier
            float sensitivity = settings.Landscape.Camera.MouseWheelZoomSensitivity;

            float oldSize = orthographicSize;
            orthographicSize -= yOffset * zoomSensitivity * sensitivity;
            orthographicSize = MathF.Max(1.0f, MathF.Min(orthographicSize, 100000.0f));
        }

        public void ProcessMouseScrollAtCursor(float yOffset, Vector2 mouseScreenPos, Vector2 screenSize) {
            float oldSize = orthographicSize;

            // Calculate zoom factor based on current orthographic size
            float zoomSensitivity = orthographicSize * 0.1f;

            // Apply user-configurable sensitivity multiplier
            float sensitivity = settings.Landscape.Camera.MouseWheelZoomSensitivity;

            orthographicSize -= yOffset * zoomSensitivity * sensitivity;
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

        /// <summary>
        /// Reset camera orientation to face north (yaw = 0)
        /// </summary>
        public void ResetOrientation() {
            yaw = 0.0f;
            UpdateCameraVectors();
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

        public static float Lerp(float a, float b, float t) {
            return a + (b - a) * t;
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

            // Preserve yaw when switching between perspective and orthographic
            if (currentCamera is PerspectiveCamera perspCamera && newCamera is OrthographicTopDownCamera orthoCamera) {
                orthoCamera.Yaw = perspCamera.Yaw;
            }
            else if (currentCamera is OrthographicTopDownCamera orthoFromCamera && newCamera is PerspectiveCamera perspToCamera) {
                // When switching from ortho to perspective, preserve the yaw
                var currentPos = orthoFromCamera.Position;
                perspToCamera.SetPosition(currentPos);
                perspToCamera.SetTopDownOrientation();
                // Manually set yaw after SetTopDownOrientation
                // We'll handle this in SwitchToPerspectiveFromTopDown instead
            }

            currentCamera = newCamera;
        }

        /// <summary>
        /// Update camera animations - call this every frame
        /// </summary>
        /// <param name="deltaTime">Time since last frame in seconds</param>
        public void Update(double deltaTime) {
            if (currentCamera is PerspectiveCamera perspCamera) {
                perspCamera.UpdateAnimation(deltaTime);
            }
        }

        /// <summary>
        /// Switch to top-down camera with smooth animation from current perspective
        /// </summary>
        public void SwitchToTopDownWithAnimation(ICamera topDownCamera) {
            if (currentCamera is PerspectiveCamera perspCamera) {
                // Start animation to top-down view (both pitch and yaw)
                perspCamera.AnimateToTopDown();
            }
            // Note: We don't actually switch cameras until the user zooms back in
            // The animation just rotates the perspective camera to a top-down orientation
        }

        /// <summary>
        /// Switch from top-down to perspective camera with top-down starting orientation
        /// </summary>
        public void SwitchToPerspectiveFromTopDown(ICamera perspectiveCamera, float targetAltitude = 1200f) {
            // Set position from current camera, but use a safe altitude that won't trigger auto-switch back
            var currentPos = Current.Position;
            perspectiveCamera.SetPosition(currentPos.X, currentPos.Y, targetAltitude);

            // Set to top-down orientation and preserve yaw from orthographic camera
            if (perspectiveCamera is PerspectiveCamera perspCamera) {
                if (Current is OrthographicTopDownCamera orthoCamera) {
                    perspCamera.SetTopDownOrientation(orthoCamera.Yaw);
                } else {
                    perspCamera.SetTopDownOrientation();
                }
            }

            currentCamera = perspectiveCamera;
        }
    }
}