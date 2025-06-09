using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Skia;
using Raylib_cs;
using Silk.NET.OpenGL;
using WorldBuilder.Fun;
using WorldBuilder.Lib;
using WorldBuilder.Lib.Avalonia;
using static System.Formats.Asn1.AsnWriter;
using Color = Raylib_cs.Color;
using MatrixMode = Raylib_cs.MatrixMode;

namespace WorldBuilder {
    class Program {
        private static int ScreenWidth = 800;
        private static int ScreenHeight = 450;

        private static ConcurrentQueue<Action> _actions = new ConcurrentQueue<Action>();
        private static Camera3D camera;
        private static GL? _GL;

        public static WindowManager WindowManager { get; } = new WindowManager();

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder
                .Configure<App>()
                .UseSkia();

        public static void Invoke(Action action) => _actions.Enqueue(action);

        unsafe static void Main(string[] args) {
            Raylib.InitWindow(ScreenWidth, ScreenHeight, "WorldBuilder");
            Raylib.SetWindowState(ConfigFlags.ResizableWindow);

            _GL = GL.GetApi(new SilkRaylibContext());

            var app = AppBuilder.Configure<App>()
                .UseChorizite()
                .SetupWithoutStarting();

            camera = new Camera3D {
                Position = new Vector3(0.0f, 7.0f, 7.0f),
                Target = new Vector3(0.0f, 0.0f, 0.0f),
                Up = new Vector3(0.0f, 1.0f, 0.0f),
                FovY = 45.0f,
                Projection = CameraProjection.Perspective
            };

            WindowManager.AddWindow(CreateFrameCounterControl());

            while (!Raylib.WindowShouldClose()) {
                Update();
                Render();
            }

            WindowManager.Dispose();
            Raylib.CloseWindow();
        }

        private static void Update() {
            if (Raylib.IsWindowResized()) {
                ScreenWidth = Raylib.GetScreenWidth();
                ScreenHeight = Raylib.GetScreenHeight();
            }

            while (_actions.TryDequeue(out var action)) {
                action?.Invoke();
            }

            float deltaTime = Raylib.GetFrameTime();
            WindowManager.Update();
        }

        private static void Render() {
            ResetOpenGLState();

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);

            Raylib.BeginMode3D(camera);
            Raylib.EndMode3D();

            WindowManager.Render();

            Raylib.DrawFPS(ScreenWidth - 100, 10);

            Raylib.EndDrawing();
        }

        private static void ResetOpenGLState() {
            if (_GL == null) return;

            // Enable states expected by raylib
            _GL.Enable(EnableCap.Blend);
            _GL.Enable(EnableCap.ProgramPointSize);
            _GL.Enable(EnableCap.Multisample);
            _GL.Enable(EnableCap.Dither);
            _GL.FrontFace(FrontFaceDirection.CW);
            _GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            _GL.DrawBuffer(DrawBufferMode.Back);
            _GL.DepthMask(true);

            // Disable states modified by Skia
            _GL.Disable(EnableCap.FramebufferSrgb);
            _GL.Disable(EnableCap.ScissorTest);
            _GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            // Restore buffer bindings
            _GL.BindBuffer(GLEnum.ElementArrayBuffer, 0);
            _GL.BindBuffer(GLEnum.ArrayBuffer, 0);
            _GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
            _GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
            _GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

            // Restore texture and sampler bindings
            _GL.ActiveTexture(TextureUnit.Texture0);
            _GL.BindTexture(TextureTarget.Texture2D, 0);
            _GL.BindSampler(0, 0);
            _GL.ActiveTexture(TextureUnit.Texture31);
            _GL.BindTexture(TextureTarget.Texture2D, 0);
            _GL.ActiveTexture(TextureUnit.Texture0);

            // Ensure clean shader state
            _GL.UseProgram(0);
        }

        private static RaylibAvaloniaControl CreateFrameCounterControl() {
            return new RaylibAvaloniaControl { Control = new HelloWorldView(), Position = new Vector2(10, 10), Size = new Size(300, 200) };
        }
    }
}