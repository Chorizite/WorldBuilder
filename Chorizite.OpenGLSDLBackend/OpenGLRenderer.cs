using Chorizite.Core.Dats;
using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using Chorizite.OpenGLSDLBackend.Lib;
using DatReaderWriter.DBObjs;
using FontStashSharp.Interfaces;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using System.Numerics;
using System.Runtime.InteropServices;
using Rectangle = Chorizite.Core.Render.Rectangle;

namespace Chorizite.OpenGLSDLBackend {
    unsafe public class OpenGLRenderer : BaseRenderer {
        private readonly ILogger _log;
        private readonly int _initialWidth;
        private readonly int _initialHeight;

        public override nint NativeHwnd => SDLWindowHandle;

        public override OpenGLGraphicsDevice GraphicsDevice { get; }

        public nint SDLWindowHandle { get; private set; }
        public nint SDLGLContext { get; private set; }

        public override IShader UIShader { get; }
        public override IShader TextShader { get; }

        public override IDrawList DrawList { get; }

        protected override ILogger Log => _log;

        public override IFontManager FontManager { get; }


        public OpenGLRenderer(GL gl, ILogger log, IDatReaderInterface _dat, int width, int height) {
            _log = log;
            _initialWidth = width;
            _initialHeight = height;

            GraphicsDevice = new OpenGLGraphicsDevice(gl, log) {
                Viewport = new Rectangle(0, 0, _initialWidth, _initialHeight)
            };

            UIShader = GraphicsDevice.CreateShader("UIShader", EmbeddedResourceReader.GetEmbeddedResource("Shaders.UI.vert"), EmbeddedResourceReader.GetEmbeddedResource("Shaders.UI.frag"));
            TextShader = GraphicsDevice.CreateShader("TextShader", EmbeddedResourceReader.GetEmbeddedResource("Shaders.Text.vert"), EmbeddedResourceReader.GetEmbeddedResource("Shaders.Text.frag"));
            DrawList = new DrawList(this, _dat, log);

            FontManager = new FontManager(log, GraphicsDevice, _dat);
        }

        public override void Render() {
            base.Render();
        }

        public override void SetCursor(uint cursorDataId, Vector2 hotspot) {
            throw new NotImplementedException();
        }

        public override void Dispose() {
            (UIShader as IDisposable)?.Dispose();
            (TextShader as IDisposable)?.Dispose();
            DrawList?.Dispose();
            FontManager?.Dispose();
        }
    }
}