using Avalonia.Controls;
using Raylib_cs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Lib.Avalonia;

namespace WorldBuilder.Lib {
    public class WindowManager : IDisposable {
        public List<RaylibAvaloniaControl> Windows = new List<RaylibAvaloniaControl>();

        public void AddWindow(RaylibAvaloniaControl window) {
            Windows.Add(window);
            window.Ready();
        }

        public void RemoveWindow(RaylibAvaloniaControl window) => Windows.Remove(window);

        internal void Update() {
            ProcessInput();
            RaylibPlatform.GRContext.ResetContext(SkiaSharp.GRBackendState.All);
            foreach (var window in Windows) {
                window.RenderToTexture();
            }
        }

        private void ProcessInput() {
            var mousePos = Raylib.GetMousePosition();

            foreach (var window in Windows) {
                var scaledWidth = window.Size.Width * window.RenderScaling;
                var scaledHeight = window.Size.Height * window.RenderScaling;
                var isInControl = mousePos.X >= window.Position.X && mousePos.X < window.Position.X + scaledWidth &&
                                 mousePos.Y >= window.Position.Y && mousePos.Y < window.Position.Y + scaledHeight;

                window.ProcessInput();
            }
        }

        internal void Render() {
            foreach (var window in Windows) {
                window.RenderTexture();
            }
        }

        public void Dispose() {
            foreach (var window in Windows) {
                window.Dispose();
            }
            Windows.Clear();
        }
    }
}
