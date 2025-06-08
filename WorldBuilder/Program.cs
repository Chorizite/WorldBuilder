using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Skia;
using Raylib_cs;
using WorldBuilder.Lib.Avalonia;
using static System.Formats.Asn1.AsnWriter;
using Color = Raylib_cs.Color;

namespace WorldBuilder {
    class Program {
        const int ScreenWidth = 800;
        const int ScreenHeight = 450;

        // Dimensions for Avalonia controls
        const int UiWidth = 300;
        const int UiHeight = 300;

        public static AppBuilder BuildAvaloniaApp()
        => AppBuilder
            .Configure<App>()
            .UseSkia();

        private static ConcurrentQueue<Action> _actions = new ConcurrentQueue<Action>();
        private static Camera3D camera;
        private static List<(RaylibAvaloniaControl Control, Vector2 Position)> controls;

        public static void Invoke(Action action) => _actions.Enqueue(action);

        static void Main(string[] args) {
            Raylib.InitWindow(ScreenWidth, ScreenHeight, "WorldBuilder");
            //Raylib.SetTargetFPS(60);

            var app = AppBuilder.Configure<App>()
                .UseChorizite()
                .SetupWithoutStarting();

            // Initialize 3D camera
            camera = new Camera3D {
                Position = new Vector3(0.0f, 10.0f, 10.0f),
                Target = new Vector3(0.0f, 0.0f, 0.0f),
                Up = new Vector3(0.0f, 1.0f, 0.0f),
                FovY = 45.0f,
                Projection = CameraProjection.Perspective
            };

            // Create Avalonia controls
            controls = new List<(RaylibAvaloniaControl Control, Vector2 Position)>
            {
                // Control 1: Frame counter at position (10, 10)
                (CreateFrameCounterControl(), new Vector2(0, 0)),
                // Control 2: FPS and title at position (10, 120)
                //(CreateFpsControl(), new Vector2(10, 120))
            };

            // Initialize controls
            foreach (var (control, _) in controls) {
                control.Size = new Avalonia.Size(UiWidth, UiHeight);
                control.Ready();
            }

            // UI update variables
            while (!Raylib.WindowShouldClose()) {
                DoRender();
            }

            // Cleanup
            foreach (var (control, _) in controls) {
                control.Dispose();
            }
            Raylib.CloseWindow();
        }

        private static void DoRender() {
            while (_actions.TryDequeue(out var action)) {
                action?.Invoke();
            }

            ProcessInput(controls);

            // something about this seems to corrupt the opengl state,
            // and then later when we render text with raylib, it doesnt render properly...
            // this is not a per frame thing but will happen every frame afte avalonia has
            // used skia to render.
            foreach (var (control, position) in controls) {
               control.Render(position);
            }

            ResetOpenGLState();

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);

            Raylib.BeginMode3D(camera);
            Raylib.DrawCube(new Vector3(0.0f, 0.0f, 0.0f), 2.0f, 6.0f, 2.0f, Color.Red);
            Raylib.DrawCubeWires(new Vector3(0.0f, 0.0f, 0.0f), 2.0f, 6.0f, 2.0f, Color.Gold);
            Raylib.DrawGrid(10, 1.0f);
            Raylib.EndMode3D();

            foreach (var (control, position) in controls) {
                control.RenderTexture(position);
            }

            Font font = Raylib.GetFontDefault();
            Raylib.DrawFPS(ScreenWidth - 100, 10);

            Raylib.EndDrawing();
        }

        private static void ResetOpenGLState() {
            Rlgl.DisableFramebuffer();
            Rlgl.BindFramebuffer(0, 0);

            Rlgl.Viewport(0, 0, ScreenWidth, ScreenHeight);

            Rlgl.MatrixMode(MatrixMode.Projection);
            Rlgl.LoadIdentity();
            Rlgl.Ortho(0, ScreenWidth, ScreenHeight, 0, 0.1f, 100.0f); 
            Rlgl.MatrixMode(MatrixMode.ModelView);
            Rlgl.LoadIdentity();

            Rlgl.SetTexture(0);
            Font font = Raylib.GetFontDefault();
            Rlgl.EnableTexture(font.Texture.Id);

            Rlgl.DisableShader();
            Rlgl.DisableVertexArray();

            Rlgl.EnableVertexArray(0);
            Rlgl.DisableVertexAttribute(0);
            Rlgl.DisableVertexAttribute(1);
            Rlgl.DisableVertexAttribute(2);

            Rlgl.SetBlendMode(BlendMode.Alpha);
        }

        private static RaylibAvaloniaControl CreateFrameCounterControl() {
            return new RaylibAvaloniaControl { Control = new HelloWorldView() };
        }

        private static void ProcessInput(List<(RaylibAvaloniaControl Control, Vector2 Position)> controls) {
            var mousePos = Raylib.GetMousePosition();

            foreach (var (control, position) in controls) {
                var scaledWidth = control.Size.Width * control.RenderScaling;
                var scaledHeight = control.Size.Height * control.RenderScaling;
                var isInControl = mousePos.X >= position.X && mousePos.X < position.X + scaledWidth &&
                                 mousePos.Y >= position.Y && mousePos.Y < position.Y + scaledHeight;

                control.HasFocus = isInControl;
                if (isInControl) {
                    control.ProcessInput();
                }
            }
        }
    }
}