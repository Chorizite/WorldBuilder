using Chorizite.Core.Render;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Chorizite.OpenGLSDLBackend {

    public unsafe class GLSLShader : BaseShader, IDisposable {
        private OpenGLGraphicsDevice _device;
        private Dictionary<string, int> _uniformLocations = [];
        private Dictionary<int, object> _uniformValues = [];
        private GL GL => _device.GL;
        public uint Program { get; protected set; }

        public GLSLShader(OpenGLGraphicsDevice device, string name, string vertSource, string fragSource, ILogger log) : base(name, vertSource, fragSource, log) {
            _device = device;

            Load(vertSource, fragSource);
        }

        public GLSLShader(OpenGLGraphicsDevice device, string name, string shaderDirectory, ILogger log) : base(name, shaderDirectory, log) {
            _device = device;

            Load();
        }

        public override void Dispose() {
            Unload();
            base.Dispose();
        }

        private int GetUniformLocation(uint program, string name) {
            if (!_uniformLocations.ContainsKey(name)) {
                _uniformLocations.Add(name, GL.GetUniformLocation(program, name));
                GLHelpers.CheckErrors();
            }
            return _uniformLocations[name];
        }

        public override void SetUniform(string location, Matrix4x4 m) {
            int loc = GetUniformLocation(Program, location);
            if (loc == -1) return;

            // Matrix comparison is a bit more expensive, but avoids the array allocation and GL call
            if (_uniformValues.TryGetValue(loc, out var val) && val is Matrix4x4 mCached && mCached == m) {
                return;
            }
            _uniformValues[loc] = m;

            // Use the matrix directly without creating a new float array
            GL.UniformMatrix4(loc, 1, false, (float*)&m);
            GLHelpers.CheckErrors();
        }

        public override void SetUniform(string location, int v) {
            int loc = GetUniformLocation((uint)Program, location);
            if (loc == -1) return;

            if (_uniformValues.TryGetValue(loc, out var val) && val is int vCached && vCached == v) {
                return;
            }
            _uniformValues[loc] = v;

            GL.Uniform1(loc, v);
            GLHelpers.CheckErrors();
        }

        public override void SetUniform(string location, Vector2 vec) {
            int loc = GetUniformLocation((uint)Program, location);
            if (loc == -1) return;

            if (_uniformValues.TryGetValue(loc, out var val) && val is Vector2 vCached && vCached == vec) {
                return;
            }
            _uniformValues[loc] = vec;

            GL.Uniform2(loc, vec);
            GLHelpers.CheckErrors();
        }

        public override void SetUniform(string location, Vector3 vec) {
            int loc = GetUniformLocation((uint)Program, location);
            if (loc == -1) return;

            if (_uniformValues.TryGetValue(loc, out var val) && val is Vector3 vCached && vCached == vec) {
                return;
            }
            _uniformValues[loc] = vec;

            GL.Uniform3(loc, vec);
            GLHelpers.CheckErrors();
        }


        public override void SetUniform(string location, Vector3[] vecs) {
            int loc = GetUniformLocation((uint)Program, location);
            if (loc == -1) return;

            // Arrays are trickier to cache, for now we skip caching them
            fixed (float* v = &vecs[0].X) {
                GL.Uniform3(loc, (uint)vecs.Length, v);
                GLHelpers.CheckErrors();
            }
        }

        public override void SetUniform(string location, Vector4 vec) {
            int loc = GetUniformLocation((uint)Program, location);
            if (loc == -1) return;

            if (_uniformValues.TryGetValue(loc, out var val) && val is Vector4 vCached && vCached == vec) {
                return;
            }
            _uniformValues[loc] = vec;

            GL.Uniform4(loc, vec);
            GLHelpers.CheckErrors();
        }

        public override void SetUniform(string location, float v) {
            int loc = GetUniformLocation((uint)Program, location);
            if (loc == -1) return;

            if (_uniformValues.TryGetValue(loc, out var val) && val is float vCached && vCached == v) {
                return;
            }
            _uniformValues[loc] = v;

            GL.Uniform1(loc, v);
            GLHelpers.CheckErrors();
        }

        public override void SetUniform(string location, float[] vs) {
            fixed (float* v = &vs[0]) {
                GL.Uniform1(GetUniformLocation((uint)Program, location), (uint)vs.Length, v);
                GLHelpers.CheckErrors();
            }
        }

        public override void Load(string vertShaderSource, string fragShaderSource) {

            if (string.IsNullOrWhiteSpace(vertShaderSource) || string.IsNullOrWhiteSpace(fragShaderSource)) {
                _log.LogError($"Shader {Name} has no source code!");
                return;
            }

            uint vertexShader = CompileShader(ShaderType.VertexShader, Name, vertShaderSource);
            uint fragmentShader = CompileShader(ShaderType.FragmentShader, Name, fragShaderSource);

            var prog = GL.CreateProgram();
            GLHelpers.CheckErrors(true);
            GL.AttachShader(prog, vertexShader);
            GLHelpers.CheckErrors(true);
            GL.AttachShader(prog, fragmentShader);
            GLHelpers.CheckErrors(true);
            GL.LinkProgram(prog);
            GLHelpers.CheckErrors(true);

            GL.GetProgram(prog, GLEnum.LinkStatus, out int success);
            GLHelpers.CheckErrors();
            if (success != 1) {
                var infoLog = GL.GetProgramInfoLog(prog);
                _log.LogError($"Error: shader {Name} link failed: {infoLog}");
                GL.DeleteProgram(prog);
                return;
            }
            else {
                _log.LogTrace($"{(Program != 0 ? "Reloaded" : "Loaded")} shader: {Name}");
            }

            GL.DeleteShader(vertexShader);
            GLHelpers.CheckErrors();
            GL.DeleteShader(fragmentShader);
            GLHelpers.CheckErrors();

            if (Program != 0) {
                Unload();
            }
            _uniformLocations.Clear();
            _uniformValues.Clear();

            GpuMemoryTracker.TrackResourceAllocation(GpuResourceType.Shader);
            Program = prog;
            NeedsLoad = false;
        }

        private uint CompileShader(ShaderType shaderType, string name, string shaderSource) {
            uint shader = GL.CreateShader(shaderType);
            GLHelpers.CheckErrors();

            GL.ShaderSource(shader, shaderSource);
            GLHelpers.CheckErrors();
            GL.CompileShader(shader);
            GLHelpers.CheckErrors();

            GL.GetShader(shader, ShaderParameterName.CompileStatus, out int success);
            GLHelpers.CheckErrors();
            if (success != 1) {
                var infoLog = GL.GetShaderInfoLog(shader);
                _log.LogError($"Error: {name}:{shaderType} compilation failed: {infoLog}");
            }

            return shader;
        }

        public override void Bind() {
            SetActive();
            GL.UseProgram((uint)Program);
            GLHelpers.CheckErrors();
        }

        public override void Unbind() {
            GL.UseProgram(0);
            GLHelpers.CheckErrors();
        }

        protected override void Unload() {
            if (Program != 0) {
                GL.DeleteProgram((uint)Program);
                GpuMemoryTracker.TrackResourceDeallocation(GpuResourceType.Shader);
                GLHelpers.CheckErrors();
                Program = 0;
            }
        }
    }
}