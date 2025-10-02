using Avalonia;
using Avalonia.Input;
using System;
using System.Collections;
using System.Numerics;
using WorldBuilder.Test;
using WorldBuilder.Tools;
using WorldBuilder.Tools.Landscape;

namespace WorldBuilder.Lib
{

    public class AvaloniaInputState
    {
        private readonly BitArray _keys = new((int)Key.DeadCharProcessed + 1);
        private readonly BitArray _keysPrevious = new((int)Key.DeadCharProcessed + 1);
        private MouseState _currentMouseState;

        public KeyModifiers Modifiers { get; internal set; }
        public MouseState MouseState => _currentMouseState;
        private Vector2 _lastMousePos = new();

        /// <summary>
        /// Called internally at the start of each frame (before OpenTkRender) to copy keyboard state to the previous frame's buffer.
        /// You probably don't want to call this, but it's public just-in-case.
        /// </summary>
        public void OnFrame()
        {
            _keysPrevious.SetAll(false);
            _keysPrevious.Or(_keys);
            _lastMousePos = MouseState.Position;
        }

        /// <summary>
        /// Called to set the state of a key when an input event is received.
        /// </summary>
        /// <param name="key">The key to set</param>
        /// <param name="pressed">True if the key is down, false if it is up.</param>
        public void SetKey(Key key, bool pressed) => _keys.Set((int)key, pressed);

        /// <summary>
        /// Checks if the specified key was down at the start of the current frame.
        /// </summary>
        /// <param name="key">The key to check the state of</param>
        /// <returns>True if the key was down, false if it was up.</returns>
        public bool IsKeyDown(Key key) => _keys.Get((int)key);

        /// <summary>
        /// Checks if the specified key was down at the start of the <em>previous</em> frame.
        /// </summary>
        /// <param name="key">The key to check the state of</param>
        /// <returns>True if the key was down, false if it was up.</returns>
        public bool WasKeyDownLastFrame(Key key) => _keysPrevious.Get((int)key);



        internal void UpdateMouseState(Point p, PointerPointProperties properties, int Width, int Height, Vector2 inputScale, ICamera camera, TerrainSystem provider)
        {
            Vector2 relativePos =  new Vector2((float)p.X, (float)p.Y) * inputScale;
            var hitResult = TerrainRaycast.Raycast(
                relativePos.X, relativePos.Y,
                Width, Height,
                camera,
                provider
            );
            _currentMouseState = new MouseState
            {
                Position = relativePos,
                LeftPressed = properties.IsLeftButtonPressed,
                RightPressed = properties.IsRightButtonPressed,
                MiddlePressed = properties.IsMiddleButtonPressed,
                Delta = relativePos - _lastMousePos,
                IsOverTerrain = hitResult.Hit,
                TerrainHit = hitResult.Hit ? hitResult : null
            };
        }
    }
}