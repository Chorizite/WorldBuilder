using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Lib.Platform {
    public static class PlatformMouse {
        public static bool IsCheckingBounds { get; private set; }
        private static bool _ignoreNextDelta;
        private static PixelPoint _initialMouselook;

        public static void OnPointerPressed(UserControl uc, PointerPressedEventArgs e, ViewportInputEvent ve, Action? callback = null) {
            var point = e.GetCurrentPoint(uc);
            if (point.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed) {
                IsCheckingBounds = true;
                if (Platform.IsWindows || Platform.IsLinux) {
                    _initialMouselook = uc.PointToScreen(point.Position);
                }
                else if (Platform.IsMacOS) {
                    MacOSMouse.DisassociateMouseAndCursor();
                    // Clear any initial delta caused by disassociation
                    MacOSMouse.GetLastMouseDelta(out int rawDeltaX, out int rawDeltaY);
                }
                uc.Cursor = new Cursor(StandardCursorType.None);
                if (callback != null)
                    callback();
            }
        }

        public static bool OnPointerMoved(UserControl uc, PointerEventArgs e, ViewportInputEvent ve) {
            if (!IsCheckingBounds) return true;

            if (Platform.IsWindows || Platform.IsLinux) {
                var currentPosition = e.GetPosition(uc);
                var hitTestResult = uc.InputHitTest(currentPosition);

                if (_ignoreNextDelta) {
                    const double SMALL_DELTA_THRESHOLD = 3.0;

                    if (Math.Abs(ve.Delta.X) > SMALL_DELTA_THRESHOLD || Math.Abs(ve.Delta.Y) > SMALL_DELTA_THRESHOLD) {
                        _ignoreNextDelta = false;
                    }
                    return false;
                }
                if (hitTestResult == null) {
                    // Set flag to ignore the next delta caused by SetCursorPos
                    _ignoreNextDelta = true;
                    MouseLook_Reset();
                }
            }
            else if (Platform.IsMacOS) {
                // On macOS, when cursor is disassociated, use raw mouse delta
                MacOSMouse.GetLastMouseDelta(out int rawDeltaX, out int rawDeltaY);
                ve.Delta = new Vector2(rawDeltaX, rawDeltaY);
            }
            return true;
        }

        public static void OnPointerReleased(UserControl uc, PointerReleasedEventArgs e, Action? callback = null) {
            if (e.InitialPressMouseButton == MouseButton.Right) {
                IsCheckingBounds = false;
                if (Platform.IsWindows || Platform.IsLinux) {
                    MouseLook_Reset();
                }
                else if (Platform.IsMacOS) {
                    MacOSMouse.AssociateMouseAndCursor();
                }
                uc.Cursor = new Cursor(StandardCursorType.Arrow);
                if (callback != null)
                    callback();
            }
        }

        private static void MouseLook_Reset() {
            if (Platform.IsWindows) {
                WindowsMouse.SetCursorPos(_initialMouselook.X, _initialMouselook.Y);
            }
            else if (Platform.IsLinux) {
                LinuxX11Mouse.SetCursorPos(_initialMouselook.X, _initialMouselook.Y);
            }
        }
    }
}
