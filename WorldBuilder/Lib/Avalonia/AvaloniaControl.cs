using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.VisualTree;
using Raylib_cs;
using AvControl = Avalonia.Controls.Control;
using Color = Raylib_cs.Color;
using MouseButton = Raylib_cs.MouseButton;
using Point = Avalonia.Point;
using Size = Avalonia.Size;

namespace WorldBuilder.Lib.Avalonia {
    public class RaylibAvaloniaControl : IDisposable {
        private IMouseDevice _mouseDevice; 
        private IKeyboardDevice _keyboardDevice; 
        private AvControl? _control; 
        private double _renderScaling = 1;
        private RaylibTopLevel? _topLevel;
        private bool _hasFocus = false;
        private bool _mouseIsOver = false;
        private RenderTexture2D? _renderTarget;
        private bool _autoConvertUIActions = false;



        public Size Size { get; set; } = new Size(300, 300);

        public AvControl? Control {
            get => _control;
            set {
                if (ReferenceEquals(_control, value))
                    return;
                _control = value;
                Console.WriteLine($"Control set to: {_control?.GetType().Name ?? "null"}");
                if (_topLevel is not null)
                    _topLevel.Content = _control;
            }
        }

        public double RenderScaling {
            get => _renderScaling;
            set {
                if (_renderScaling == value)
                    return;
                _renderScaling = value;
                if (_topLevel is not null) {
                    UpdateRenderTarget();
                    RenderAvalonia();
                }
            }
        }

        public bool HasFocus {
            get => _hasFocus;
            set {
                if (_hasFocus == value)
                    return;
                _hasFocus = value;
                if (_hasFocus)
                    OnFocusEntered();
                else
                    OnFocusExited();
            }
        }

        public bool AutoConvertUIActionToKeyDown {
            get => _autoConvertUIActions;
            set => _autoConvertUIActions = value;
        }

        public RaylibAvaloniaControl() {
        }

        public RaylibTopLevel GetTopLevel() =>
            _topLevel ?? throw new InvalidOperationException($"The {nameof(RaylibAvaloniaControl)} isn't initialized");

        public RenderTexture2D GetTexture() =>
            _renderTarget ?? throw new InvalidOperationException("Render target not initialized");

        public virtual void Ready() {
            try {
                var locator = AvaloniaLocator.Current;
                if (locator.GetService<IPlatformGraphics>() is not RaylibPlatformGraphics graphics) {
                    Console.WriteLine("Error: No Raylib platform graphics found");
                    return;
                }
                _mouseDevice = locator.GetRequiredService<IMouseDevice>();
                _keyboardDevice = locator.GetRequiredService<IKeyboardDevice>();

                _renderTarget = Raylib.LoadRenderTexture((int)Size.Width, (int)Size.Height);

                var topLevelImpl = new RaylibTopLevelImpl(
                    _renderTarget.Value,
                    graphics,
                    locator.GetRequiredService<IClipboard>(),
                    RaylibPlatform.Compositor
                ) {
                    CursorChanged = OnAvaloniaCursorChanged
                };

                topLevelImpl.SetRenderSize(GetFrameSize(), RenderScaling);

                _topLevel = new RaylibTopLevel(topLevelImpl) {
                    Background = Brushes.Transparent, // Explicitly set transparent background
                    Content = Control,
                    TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent }
                };

                _topLevel.Prepare();
                if (_topLevel is IInputRoot inputRoot) {
                    _topLevel.Impl._inputRoot = inputRoot;
                }

                _topLevel.StartRendering();
                _topLevel.PointerPressed += (s, e) => Console.WriteLine($"TopLevel PointerPressed: {e.GetPosition(_topLevel)}, Handled: {e.Handled}");
                _topLevel.PointerReleased += (s, e) => Console.WriteLine($"TopLevel PointerReleased: {e.GetPosition(_topLevel)}");

                Console.WriteLine("Raylib Avalonia control initialized successfully");
                LogVisualTree(_control);
            }
            catch (Exception ex) {
                Console.WriteLine($"Failed to initialize Raylib Avalonia control: {ex.Message}");
                throw;
            }
        }

        private void LogVisualTree(AvControl? control, int depth = 0) {
            if (control == null) return;
            Console.WriteLine(new string(' ', depth * 2) +
                $"Control: {control.GetType().Name}, Bounds: {control.Bounds}, " +
                $"Focusable: {control.Focusable}, IsHitTestVisible: {control.IsHitTestVisible}");

            foreach (var child in control.GetVisualChildren().OfType<AvControl>())
                LogVisualTree(child, depth + 1);
        }

        public void ProcessInput() {
            if (_topLevel?.Impl is not RaylibTopLevelImpl impl) {
                Console.WriteLine("Cannot process input: TopLevel or Impl is null");
                return;
            }

            var timestamp = (ulong)(Raylib.GetTime());
            var mousePos = Raylib.GetMousePosition();
            var scaledMousePos = new Vector2(mousePos.X / (float)RenderScaling, mousePos.Y / (float)RenderScaling);
            var isPointInControl = IsPointInControl((int)mousePos.X, (int)mousePos.Y);

            ProcessMouseInput(impl, scaledMousePos, timestamp, isPointInControl);
            ProcessKeyboardInput(impl, timestamp);
            UpdateMouseOverState(isPointInControl, _topLevel?.InputHitTest(new Point(scaledMousePos.X, scaledMousePos.Y)) != null);
        }

        private void ProcessMouseInput(RaylibTopLevelImpl impl, Vector2 scaledMousePos, ulong timestamp, bool isPointInControl) {
            if (impl._inputRoot == null || impl.Input == null)
                return;

            // Mouse movement
            var pointerPoint = CreateRawPointerPoint(scaledMousePos, 1.0f, Vector2.Zero);
            var modifiers = GetCurrentMouseModifiers();
            var moveArgs = new RawPointerEventArgs(
                _mouseDevice,
                timestamp,
                impl._inputRoot,
                RawPointerEventType.Move,
                pointerPoint,
                modifiers
            );
            impl.Input.Invoke(moveArgs);

            ProcessMouseButtons(impl, pointerPoint, timestamp, modifiers);

            var wheelDelta = Raylib.GetMouseWheelMove();
            if (wheelDelta != 0) {
                var wheelArgs = new RawMouseWheelEventArgs(
                    _mouseDevice,
                    timestamp,
                    impl._inputRoot,
                    new Point(scaledMousePos.X, scaledMousePos.Y),
                    new Vector2(0, wheelDelta * 120),
                    modifiers
                );
                impl.Input.Invoke(wheelArgs);
            }
        }

        private void ProcessMouseButtons(RaylibTopLevelImpl impl, RawPointerPoint pointerPoint, ulong timestamp, RawInputModifiers modifiers) {
            if (Raylib.IsMouseButtonPressed(MouseButton.Left)) {
                var args = new RawPointerEventArgs(_mouseDevice, timestamp, impl._inputRoot, RawPointerEventType.LeftButtonDown, pointerPoint, modifiers);
                impl.Input?.Invoke(args);
                if (!args.Handled)
                    HandleFocusOnClick(new Vector2((float)pointerPoint.Position.X, (float)pointerPoint.Position.Y));
            }
            else if (Raylib.IsMouseButtonReleased(MouseButton.Left)) {
                var args = new RawPointerEventArgs(_mouseDevice, timestamp, impl._inputRoot, RawPointerEventType.LeftButtonUp, pointerPoint, modifiers);
                impl.Input?.Invoke(args);
            }

            if (Raylib.IsMouseButtonPressed(MouseButton.Right)) {
                var args = new RawPointerEventArgs(_mouseDevice, timestamp, impl._inputRoot, RawPointerEventType.RightButtonDown, pointerPoint, modifiers);
                impl.Input?.Invoke(args);
            }
            else if (Raylib.IsMouseButtonReleased(MouseButton.Right)) {
                var args = new RawPointerEventArgs(_mouseDevice, timestamp, impl._inputRoot, RawPointerEventType.RightButtonUp, pointerPoint, modifiers);
                impl.Input?.Invoke(args);
            }

            if (Raylib.IsMouseButtonPressed(MouseButton.Middle)) {
                var args = new RawPointerEventArgs(_mouseDevice, timestamp, impl._inputRoot, RawPointerEventType.MiddleButtonDown, pointerPoint, modifiers);
                impl.Input?.Invoke(args);
            }
            else if (Raylib.IsMouseButtonReleased(MouseButton.Middle)) {
                var args = new RawPointerEventArgs(_mouseDevice, timestamp, impl._inputRoot, RawPointerEventType.MiddleButtonUp, pointerPoint, modifiers);
                impl.Input?.Invoke(args);
            }
        }

        private void ProcessKeyboardInput(RaylibTopLevelImpl impl, ulong timestamp) {
            if (impl._inputRoot == null || impl.Input == null) {
                Console.WriteLine("Cannot process keyboard input: InputRoot or Input is null");
                return;
            }

            var modifiers = GetCurrentKeyModifiers();
            var focusedElement = _topLevel?.FocusManager?.GetFocusedElement();

            int key = Raylib.GetKeyPressed();
            while (key != 0) {
                var keyboardKey = (KeyboardKey)key;
                var avKey = MapRaylibKeyToAvalonia(keyboardKey);
                if (avKey != Key.None) {
                    if (focusedElement != null) {
                        var keyArgs = new KeyEventArgs {
                            RoutedEvent = InputElement.KeyDownEvent,
                            Key = avKey,
                            PhysicalKey = (PhysicalKey)avKey,
                            KeyDeviceType = KeyDeviceType.Keyboard,
                            KeyModifiers = modifiers
                        };
                        focusedElement.RaiseEvent(keyArgs);
                    }
                }
                key = Raylib.GetKeyPressed();
            }

            foreach (KeyboardKey keyboardKey in Enum.GetValues(typeof(KeyboardKey))) {
                if (Raylib.IsKeyReleased(keyboardKey)) {
                    var avKey = MapRaylibKeyToAvalonia(keyboardKey);
                    if (avKey != Key.None) {
                        if (focusedElement != null) {
                            var keyArgs = new KeyEventArgs {
                                RoutedEvent = InputElement.KeyUpEvent,
                                Key = avKey,
                                PhysicalKey = (PhysicalKey)avKey,
                                KeyDeviceType = KeyDeviceType.Keyboard,
                                KeyModifiers = modifiers
                            };
                            focusedElement.RaiseEvent(keyArgs);
                        }
                    }
                }
            }

            int charPressed = Raylib.GetCharPressed();
            while (charPressed != 0) {
                var text = ((char)charPressed).ToString();
                var textArgs = new RawTextInputEventArgs(
                    _keyboardDevice,
                    timestamp,
                    impl._inputRoot,
                    text
                );

                if (focusedElement != null) {
                    focusedElement.RaiseEvent(new TextInputEventArgs {
                        RoutedEvent = InputElement.TextInputEvent,
                        Text = text
                    });
                }
                else {
                    impl.Input.Invoke(textArgs);
                }

                charPressed = Raylib.GetCharPressed();
            }
        }

        private RawPointerPoint CreateRawPointerPoint(Vector2 position, float pressure, Vector2 tilt) => new() {
            Position = new Point(position.X, position.Y),
            Twist = 0.0f,
            Pressure = pressure,
            XTilt = tilt.X * 90.0f,
            YTilt = tilt.Y * 90.0f
        };

        private RawInputModifiers GetCurrentMouseModifiers() {
            var modifiers = RawInputModifiers.None;
            if (Raylib.IsMouseButtonDown(MouseButton.Left))
                modifiers |= RawInputModifiers.LeftMouseButton;
            if (Raylib.IsMouseButtonDown(MouseButton.Right))
                modifiers |= RawInputModifiers.RightMouseButton;
            if (Raylib.IsMouseButtonDown(MouseButton.Middle))
                modifiers |= RawInputModifiers.MiddleMouseButton;
            return modifiers;
        }

        private KeyModifiers GetCurrentKeyModifiers() {
            var modifiers = KeyModifiers.None;
            if (Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl))
                modifiers |= KeyModifiers.Control;
            if (Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift))
                modifiers |= KeyModifiers.Shift;
            if (Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt))
                modifiers |= KeyModifiers.Alt;
            if (Raylib.IsKeyDown(KeyboardKey.LeftSuper) || Raylib.IsKeyDown(KeyboardKey.RightSuper))
                modifiers |= KeyModifiers.Meta;
            return modifiers;
        }

        private Key MapRaylibKeyToAvalonia(KeyboardKey raylibKey) {
            return raylibKey switch {
                KeyboardKey.Space => Key.Space,
                KeyboardKey.Apostrophe => Key.OemQuotes,
                KeyboardKey.Comma => Key.OemComma,
                KeyboardKey.Minus => Key.OemMinus,
                KeyboardKey.Period => Key.OemPeriod,
                KeyboardKey.Slash => Key.OemQuestion,
                KeyboardKey.Zero => Key.D0,
                KeyboardKey.One => Key.D1,
                KeyboardKey.Two => Key.D2,
                KeyboardKey.Three => Key.D3,
                KeyboardKey.Four => Key.D4,
                KeyboardKey.Five => Key.D5,
                KeyboardKey.Six => Key.D6,
                KeyboardKey.Seven => Key.D7,
                KeyboardKey.Eight => Key.D8,
                KeyboardKey.Nine => Key.D9,
                KeyboardKey.Semicolon => Key.OemSemicolon,
                KeyboardKey.Equal => Key.OemPlus,
                KeyboardKey.A => Key.A,
                KeyboardKey.B => Key.B,
                KeyboardKey.C => Key.C,
                KeyboardKey.D => Key.D,
                KeyboardKey.E => Key.E,
                KeyboardKey.F => Key.F,
                KeyboardKey.G => Key.G,
                KeyboardKey.H => Key.H,
                KeyboardKey.I => Key.I,
                KeyboardKey.J => Key.J,
                KeyboardKey.K => Key.K,
                KeyboardKey.L => Key.L,
                KeyboardKey.M => Key.M,
                KeyboardKey.N => Key.N,
                KeyboardKey.O => Key.O,
                KeyboardKey.P => Key.P,
                KeyboardKey.Q => Key.Q,
                KeyboardKey.R => Key.R,
                KeyboardKey.S => Key.S,
                KeyboardKey.T => Key.T,
                KeyboardKey.U => Key.U,
                KeyboardKey.V => Key.V,
                KeyboardKey.W => Key.W,
                KeyboardKey.X => Key.X,
                KeyboardKey.Y => Key.Y,
                KeyboardKey.Z => Key.Z,
                KeyboardKey.LeftBracket => Key.OemOpenBrackets,
                KeyboardKey.Backslash => Key.OemPipe,
                KeyboardKey.RightBracket => Key.OemCloseBrackets,
                KeyboardKey.Grave => Key.OemTilde,
                KeyboardKey.Escape => Key.Escape,
                KeyboardKey.Enter => Key.Enter,
                KeyboardKey.Tab => Key.Tab,
                KeyboardKey.Backspace => Key.Back,
                KeyboardKey.Insert => Key.Insert,
                KeyboardKey.Delete => Key.Delete,
                KeyboardKey.Right => Key.Right,
                KeyboardKey.Left => Key.Left,
                KeyboardKey.Down => Key.Down,
                KeyboardKey.Up => Key.Up,
                KeyboardKey.PageUp => Key.PageUp,
                KeyboardKey.PageDown => Key.PageDown,
                KeyboardKey.Home => Key.Home,
                KeyboardKey.End => Key.End,
                KeyboardKey.CapsLock => Key.CapsLock,
                KeyboardKey.ScrollLock => Key.Scroll,
                KeyboardKey.NumLock => Key.NumLock,
                KeyboardKey.PrintScreen => Key.PrintScreen,
                KeyboardKey.Pause => Key.Pause,
                KeyboardKey.F1 => Key.F1,
                KeyboardKey.F2 => Key.F2,
                KeyboardKey.F3 => Key.F3,
                KeyboardKey.F4 => Key.F4,
                KeyboardKey.F5 => Key.F5,
                KeyboardKey.F6 => Key.F6,
                KeyboardKey.F7 => Key.F7,
                KeyboardKey.F8 => Key.F8,
                KeyboardKey.F9 => Key.F9,
                KeyboardKey.F10 => Key.F10,
                KeyboardKey.F11 => Key.F11,
                KeyboardKey.F12 => Key.F12,
                KeyboardKey.LeftShift => Key.LeftShift,
                KeyboardKey.LeftControl => Key.LeftCtrl,
                KeyboardKey.LeftAlt => Key.LeftAlt,
                KeyboardKey.LeftSuper => Key.LWin,
                KeyboardKey.RightShift => Key.RightShift,
                KeyboardKey.RightControl => Key.RightCtrl,
                KeyboardKey.RightAlt => Key.RightAlt,
                KeyboardKey.RightSuper => Key.RWin,
                _ => Key.None
            };
        }

        private void HandleFocusOnClick(Vector2 scaledMousePos) {
            if (_topLevel == null)
                return;

            var point = new Point(scaledMousePos.X, scaledMousePos.Y);
            var hitControl = _topLevel.InputHitTest(point);
            IInputElement? focusTarget = hitControl as IInputElement;

            while (focusTarget != null && !focusTarget.Focusable) {
                focusTarget = focusTarget switch {
                    Visual visual => visual.GetVisualParent() as IInputElement,
                    _ => null
                };
            }

            if (focusTarget != null) {
                focusTarget.Focus(NavigationMethod.Pointer);
            }
            else {
                _topLevel.Focus();
            }
        }

        private void UpdateMouseOverState(bool isPointInControl, bool hitControl) {
            if (isPointInControl && hitControl && !_mouseIsOver) {
                _mouseIsOver = true;
            }
            else if ((!isPointInControl || !hitControl) && _mouseIsOver) {
                _mouseIsOver = false;
                if (_topLevel?.Impl is RaylibTopLevelImpl impl && impl._inputRoot != null && impl.Input != null) {
                    var args = new RawPointerEventArgs(
                        _mouseDevice,
                        (ulong)(Raylib.GetTime() * 1000),
                        impl._inputRoot,
                        RawPointerEventType.LeaveWindow,
                        new Point(-1, -1),
                        GetCurrentMouseModifiers()
                    );
                    impl.Input.Invoke(args);
                }
            }
        }

        private bool IsPointInControl(int x, int y) {
            var scaledWidth = Size.Width * RenderScaling;
            var scaledHeight = Size.Height * RenderScaling;
            return x >= 0 && y >= 0 && x < scaledWidth && y < scaledHeight;
        }

        private void OnFocusEntered() {
        }

        private void OnFocusExited() {
            
        }

        private void OnAvaloniaCursorChanged(RaylibStandardCursorImpl cursor) {
            //Raylib.SetMouseCursor(cursor.RaylibCursor);
        }

        public bool HasPoint(Point point) {
            return _topLevel?.InputHitTest(point / _renderScaling) is not null;
        }

        private PixelSize GetFrameSize() => PixelSize.FromSize(Size, 1.0);

        internal void RenderAvalonia() => _topLevel?.Impl.OnDraw(new Rect(Size));

        public void Render(Vector2 position) {
            if (_topLevel?.Impl is not RaylibTopLevelImpl impl || _renderTarget == null) {
                Console.WriteLine("Cannot render: TopLevel or render target is null");
                return;
            }

            impl.Render();
        }

        public void RenderTexture(Vector2 position) {
            if (_topLevel?.Impl is not RaylibTopLevelImpl impl || _renderTarget == null) {
                Console.WriteLine("Cannot render: TopLevel or render target is null");
                return;
            }

            var sourceRect = new Rectangle(0, 0, _renderTarget.Value.Texture.Width, -_renderTarget.Value.Texture.Height); // Flip Y for correct orientation
            var destRect = new Rectangle((int)position.X, (int)position.Y,
                (int)(Size.Width * RenderScaling), (int)(Size.Height * RenderScaling));

            Raylib.DrawTexturePro(
                impl._surface.Texture.Texture,
                sourceRect,
                destRect,
                Vector2.Zero,
                0.0f,
                Color.White // Ensure no tinting that could affect transparency
            );
        }

        private void UpdateRenderTarget() {
            if (_renderTarget != null) {
                Raylib.UnloadRenderTexture(_renderTarget.Value);
                _renderTarget = Raylib.LoadRenderTexture((int)Size.Width, (int)Size.Height);

                if (_topLevel?.Impl is RaylibTopLevelImpl impl) {
                    impl.UpdateRenderTarget(_renderTarget.Value);
                }
            }
        }

        public void Dispose() {
            if (_renderTarget != null) {
                Raylib.UnloadRenderTexture(_renderTarget.Value);
                _renderTarget = null;
            }

            _topLevel?.Dispose();
            _topLevel = null;
        }
    }

    internal static class InputExtensions {
        public static KeyModifiers ToKeyModifiers(this RawInputModifiers rawModifiers) {
            var modifiers = KeyModifiers.None;
            if (rawModifiers.HasFlag(RawInputModifiers.Alt))
                modifiers |= KeyModifiers.Alt;
            if (rawModifiers.HasFlag(RawInputModifiers.Control))
                modifiers |= KeyModifiers.Control;
            if (rawModifiers.HasFlag(RawInputModifiers.Shift))
                modifiers |= KeyModifiers.Shift;
            if (rawModifiers.HasFlag(RawInputModifiers.Meta))
                modifiers |= KeyModifiers.Meta;
            return modifiers;
        }
    }

}