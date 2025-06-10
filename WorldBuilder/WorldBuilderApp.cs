using Avalonia;
using Avalonia.Controls.Shapes;
using Raylib_cs;
using Silk.NET.OpenGL;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Fun;
using WorldBuilder.Lib;
using WorldBuilder.Lib.Avalonia;
using WorldBuilder.Views;

namespace WorldBuilder {
    public  class WorldBuilderApp : IDisposable {
        private GL _GL;
        private Camera3D camera;
        private ModelGroupRenderer _multi;
        private Tayne _tayne;
        private static ConcurrentQueue<Action> _actions = new ConcurrentQueue<Action>();
        public static void Invoke(Action action) => _actions.Enqueue(action);

        public int Width { get; private set; } = 800;
        public int Height { get; private set;} = 400;

        public WindowManager WindowManager { get; private set; }

        public WorldBuilderApp(int width, int height) {
            //WindowManager = new WindowManager();
            Width = width;
            Height = height;

            Console.WriteLine($"Current Directory: {Environment.CurrentDirectory}");
            foreach (string file in Directory.EnumerateFiles(Environment.CurrentDirectory, "*.*", SearchOption.AllDirectories)) {
                Console.WriteLine(file);
            }

            Raylib.InitWindow(Width, Height, "WorldBuilder");
            if (!OperatingSystem.IsBrowser()) {
                Raylib.SetWindowState(ConfigFlags.ResizableWindow);
            }
            _GL = GL.GetApi(new SilkRaylibContext());

            //var app = AppBuilder.Configure<App>()
            //    .UseChorizite()
            //    .SetupWithoutStarting();

            //WindowManager.AddWindow(CreateFrameCounterControl());

            camera = new Camera3D {
                Position = new Vector3(0.0f, 0.0f, 7.0f),
                Target = new Vector3(0.0f, 0.0f, 0.0f),
                Up = new Vector3(0.0f, 1.0f, 0.0f),
                FovY = 45.0f,
                Projection = CameraProjection.Perspective
            };

            _multi = new ModelGroupRenderer();
            _tayne = new Tayne("Resources/Textures/tayne.gif", new Vector3(0, -2f, 0));
        }

        public void Resize(int width, int height) {
            Width = width;
            Height = height;
            Raylib.SetWindowSize(width, height);
        }

        public void Update() {
            if (!OperatingSystem.IsBrowser() && Raylib.IsWindowResized()) {
                Width = Raylib.GetScreenWidth();
                Height = Raylib.GetScreenHeight();
            }

            camera.Position = new Vector3(0.0f, 1.3f, 7.0f);

            var delta = Raylib.GetFrameTime();
            _multi?.Update(delta, camera.Position);
            _tayne?.Update(delta);

            WindowManager?.Update();
        }

        public void Render() {
            ResetOpenGLState();

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);

            Raylib.BeginMode3D(camera);
            Rlgl.DisableBackfaceCulling();
            _multi?.Render(camera);
            _tayne?.Render(camera);
            Raylib.EndMode3D();

            WindowManager?.Render();

            Raylib.DrawFPS(10, 10);

            Raylib.EndDrawing();
        }

        private void ResetOpenGLState() {
            if (_GL == null || OperatingSystem.IsBrowser()) return;

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
            return new RaylibAvaloniaControl { Control = new HelloWorldView(), Position = new Vector2(10, 50), Size = new Size(300, 200) };
        }

        public void Dispose() {
            Raylib.CloseWindow();
        }
    }
}
