using Chorizite.Core.Render;
using Chorizite.OpenGLSDLBackend;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Tools {
    public interface ITool : IDisposable {
        public string Name { get; }
        public void Init(Project project, OpenGLRenderer render);
        public void Update(double deltaTime, AvaloniaInputState keyboard);
        public void Render();
    }
}
