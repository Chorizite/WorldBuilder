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
        private readonly object _lock = new();
        private GL GL => _device.GL;
        public uint Program { get; protected set; }

        public bool HasUniform(string name) {
            lock (_lock) {
                return GetUniformLocation(Program, name) != -1;
            }
        }

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
            lock (_lock) {
                if (!_uniformLocations.ContainsKey(name)) {
                    _uniformLocations.Add(name, GL.GetUniformLocation(program, name));
                }
                return _uniformLocations[name];
            }
        }

        public override void SetUniform(string location, Matrix4x4 m) {
            lock (_lock) {
                int loc = GetUniformLocation(Program, location);
                if (loc == -1) return;

                if (_uniformValues.TryGetValue(loc, out var val) && val is Matrix4x4 mCached && mCached == m) {
                    return;
                }
                _uniformValues[loc] = m;

                GL.UniformMatrix4(loc, 1, false, (float*)&m);
            }
        }

        public override void SetUniform(string location, int v) {
            lock (_lock) {
                int loc = GetUniformLocation((uint)Program, location);
                if (loc == -1) return;

                if (_uniformValues.TryGetValue(loc, out var val) && val is int vCached && vCached == v) {
                    return;
                }
                _uniformValues[loc] = v;

                GL.Uniform1(loc, v);
            }
        }

        public override void SetUniform(string location, Vector2 vec) {
            lock (_lock) {
                int loc = GetUniformLocation((uint)Program, location);
                if (loc == -1) return;

                if (_uniformValues.TryGetValue(loc, out var val) && val is Vector2 vCached && vCached == vec) {
                    return;
                }
                _uniformValues[loc] = vec;

                GL.Uniform2(loc, vec);
            }
        }

        public override void SetUniform(string location, Vector3 vec) {
            lock (_lock) {
                int loc = GetUniformLocation((uint)Program, location);
                if (loc == -1) return;

                if (_uniformValues.TryGetValue(loc, out var val) && val is Vector3 vCached && vCached == vec) {
                    return;
                }
                _uniformValues[loc] = vec;

                GL.Uniform3(loc, vec);
            }
        }


        public override void SetUniform(string location, Vector3[] vecs) {
            lock (_lock) {
                int loc = GetUniformLocation((uint)Program, location);
                if (loc == -1) return;

                fixed (float* v = &vecs[0].X) {
                    GL.Uniform3(loc, (uint)vecs.Length, v);
                }
            }
        }

        public override void SetUniform(string location, Vector4 vec) {
            lock (_lock) {
                int loc = GetUniformLocation((uint)Program, location);
                if (loc == -1) return;

                if (_uniformValues.TryGetValue(loc, out var val) && val is Vector4 vCached && vCached == vec) {
                    return;
                }
                _uniformValues[loc] = vec;

                GL.Uniform4(loc, vec);
            }
        }

        public override void SetUniform(string location, float v) {
            lock (_lock) {
                int loc = GetUniformLocation((uint)Program, location);
                if (loc == -1) return;

                if (_uniformValues.TryGetValue(loc, out var val) && val is float vCached && vCached == v) {
                    return;
                }
                _uniformValues[loc] = v;

                GL.Uniform1(loc, v);
            }
        }

        public override void SetUniform(string location, float[] vs) {
            lock (_lock) {
                fixed (float* v = &vs[0]) {
                    GL.Uniform1(GetUniformLocation((uint)Program, location), (uint)vs.Length, v);
                }
            }
        }

        public override void Load(string vertShaderSource, string fragShaderSource) {

            if (string.IsNullOrWhiteSpace(vertShaderSource) || string.IsNullOrWhiteSpace(fragShaderSource)) {
                _log.LogError($"Shader {Name} has no source code!");
                return;
            }

            if (_device.HasOpenGL43 && _device.HasBindless) {
                string replacement = "#version 430 core\n#extension GL_ARB_bindless_texture : require";
                vertShaderSource = vertShaderSource.Replace("#version 330 core", replacement);
                fragShaderSource = fragShaderSource.Replace("#version 330 core", replacement);
            }

            uint vertexShader = CompileShader(ShaderType.VertexShader, Name, vertShaderSource);
            uint fragmentShader = CompileShader(ShaderType.FragmentShader, Name, fragShaderSource);

            var prog = GL.CreateProgram();
            GLHelpers.CheckErrors(GL, true);
            GL.AttachShader(prog, vertexShader);
            GLHelpers.CheckErrors(GL, true);
            GL.AttachShader(prog, fragmentShader);
            GLHelpers.CheckErrors(GL, true);
            GL.LinkProgram(prog);
            GLHelpers.CheckErrors(GL, true);

            GL.GetProgram(prog, GLEnum.LinkStatus, out int success);
            GLHelpers.CheckErrors(GL);
            if (success != 1) {
                var infoLog = GL.GetProgramInfoLog(prog);
                _log.LogError($"Error: shader {Name} link failed: {infoLog}");
                GL.DeleteProgram(prog);
                return;
            }
            else {
                _log.LogTrace($"{(Program != 0 ? "Reloaded" : "Loaded")} shader: {Name}");
            }

            // Bind SceneData uniform block to point 0 if it exists
            var sceneDataIndex = GL.GetUniformBlockIndex(prog, "SceneData");
            if (sceneDataIndex != uint.MaxValue) {
                GL.UniformBlockBinding(prog, sceneDataIndex, 0);
                GLHelpers.CheckErrors(GL);
            }

            GL.DeleteShader(vertexShader);
            GLHelpers.CheckErrors(GL);
            GL.DeleteShader(fragmentShader);
            GLHelpers.CheckErrors(GL);

            if (Program != 0) {
                Unload();
            }
            _uniformLocations.Clear();
            _uniformValues.Clear();

            GpuMemoryTracker.TrackResourceAllocation(GpuResourceType.Shader);
            Program = prog;
            ProgramId = prog;
            NeedsLoad = false;
            GLHelpers.CheckErrors(GL);
        }

        private uint CompileShader(ShaderType shaderType, string name, string shaderSource) {
            uint shader = GL.CreateShader(shaderType);
            GLHelpers.CheckErrors(GL);

            GL.ShaderSource(shader, shaderSource);
            GLHelpers.CheckErrors(GL);
            GL.CompileShader(shader);
            GLHelpers.CheckErrors(GL);

            GL.GetShader(shader, ShaderParameterName.CompileStatus, out int success);
            GLHelpers.CheckErrors(GL);
            if (success != 1) {
                var infoLog = GL.GetShaderInfoLog(shader);
                _log.LogError($"Error: {name}:{shaderType} compilation failed: {infoLog}");
            }

            return shader;
        }

        public override void Bind() {
            lock (_lock) {
                SetActive();
                if (Program != 0) {
                    GL.UseProgram((uint)Program);
                }
            }
        }

        public override void Unbind() {
            lock (_lock) {
                GL.UseProgram(0);
                GLHelpers.CheckErrors(GL);
            }
        }

        protected override void Unload() {
            lock (_lock) {
                if (Program != 0) {
                    var prog = Program;
                    Program = 0;
                    ProgramId = 0;
                    GL.DeleteProgram(prog);
                    GpuMemoryTracker.TrackResourceDeallocation(GpuResourceType.Shader);
                    GLHelpers.CheckErrors(GL);
                }
            }
        }
    }
}
