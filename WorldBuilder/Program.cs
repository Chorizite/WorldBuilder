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
using WorldBuilder.Lib;
using WorldBuilder.Lib.Avalonia;
using static System.Formats.Asn1.AsnWriter;
using Color = Raylib_cs.Color;
using MatrixMode = Raylib_cs.MatrixMode;

namespace WorldBuilder {

    class Program {
        const int ScreenWidth = 800;
        const int ScreenHeight = 450;

        public static AppBuilder BuildAvaloniaApp()
        => AppBuilder
            .Configure<App>()
            .UseSkia();

        private static ConcurrentQueue<Action> _actions = new ConcurrentQueue<Action>();
        private static Camera3D camera;
        private static Raylib_cs.Image _image;
        private static Texture2D _texture;
        private static GL? _GL;

        public static WindowManager WindowManager { get; } = new WindowManager();

        public static void Invoke(Action action) => _actions.Enqueue(action);

        unsafe static void Main(string[] args) {
            Raylib.InitWindow(ScreenWidth, ScreenHeight, "WorldBuilder");
            Raylib.SetWindowState(ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint);

            _GL = GL.GetApi(new SilkRaylibContext());

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
            _image = Raylib.LoadImage("avalonia.png");
            _texture = Raylib.LoadTexture("avalonia.png");

            WindowManager.AddWindow(CreateFrameCounterControl());

            // UI update variables
            while (!Raylib.WindowShouldClose()) {
                Update();
                Render();
            }

            WindowManager.Dispose();
            Raylib.CloseWindow();
        }

        private static void Update() {
            while (_actions.TryDequeue(out var action)) {
                action?.Invoke();
            }

            WindowManager.Update();
        }

        private static void Render() {
            ResetOpenGLState();

            Raylib.BeginDrawing();

            Raylib.ClearBackground(Color.DarkGray);

            Raylib.BeginMode3D(camera);
            Raylib.DrawCube(new Vector3(0.0f, 0.0f, 0.0f), 2.0f, 6.0f, 2.0f, Color.Red);
            Raylib.DrawCubeWires(new Vector3(0.0f, 0.0f, 0.0f), 2.0f, 6.0f, 2.0f, Color.Gold);
            Raylib.DrawGrid(10, 1.0f);
            Raylib.EndMode3D();

            WindowManager.Render();

            Raylib.DrawText("Hello World!", 5, 5, 22, Color.Gold);
            Raylib.DrawFPS(ScreenWidth - 100, 10);
            Raylib.DrawTexturePro(
                _texture,
                new Rectangle(0, 0, _image.Width, _image.Height),
                new Rectangle(20, 20, 50, 50),
                Vector2.Zero,
                0.0f,
                Color.White
                );

            Raylib.EndDrawing();
        }

        private static void ResetOpenGLState() {
            if (_GL == null) return;

            // enable states expected by raylib
            _GL.Enable(EnableCap.Blend);
            _GL.Enable(EnableCap.ProgramPointSize);
            _GL.Enable(EnableCap.Multisample);
            _GL.Enable(EnableCap.Dither);
            _GL.FrontFace(FrontFaceDirection.CW);
            _GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            _GL.DrawBuffer(DrawBufferMode.Back);
            _GL.DepthMask(true);

            // disable states modified by Skia
            _GL.Disable(EnableCap.FramebufferSrgb);
            _GL.Disable(EnableCap.ScissorTest);
            _GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            // restore buffer bindings
            _GL.BindBuffer(GLEnum.ElementArrayBuffer, 0);
            _GL.BindBuffer(GLEnum.ArrayBuffer, 0);
            _GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
            _GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
            _GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

            // restore texture and sampler bindings
            _GL.ActiveTexture(TextureUnit.Texture0);
            _GL.BindTexture(TextureTarget.Texture2D, 0);
            _GL.BindSampler(0, 0);
            _GL.ActiveTexture(TextureUnit.Texture31);
            _GL.BindTexture(TextureTarget.Texture2D, 0);
            _GL.ActiveTexture(TextureUnit.Texture0);

        }

        private static RaylibAvaloniaControl CreateFrameCounterControl() {
            return new RaylibAvaloniaControl { Control = new HelloWorldView(), Position = new Vector2(10, 10), Size = new Size(ScreenWidth - 20, ScreenHeight - 20) };
        }
    }
}