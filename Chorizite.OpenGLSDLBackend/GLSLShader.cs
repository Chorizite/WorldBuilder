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

    public unsafe class GLSLShader : BaseShader {
        private OpenGLGraphicsDevice _device;
        private Dictionary<string, int> _uniformLocations = [];
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

        private int GetUniformLocation(uint program, string name) {
            if (!_uniformLocations.ContainsKey(name)) {
                _uniformLocations.Add(name, GL.GetUniformLocation(program, name));
            }
            GLHelpers.CheckErrors();
            return _uniformLocations[name];
        }

        public override void SetUniform(string location, Matrix4x4 m) {
            var m2 = new float[] {
                    m.M11, m.M12, m.M13, m.M14,
                    m.M21, m.M22, m.M23, m.M24,
                    m.M31, m.M32, m.M33, m.M34,
                    m.M41, m.M42, m.M43, m.M44
                };
            fixed (float* transform = (float[])m2) {
                GL.UniformMatrix4(GetUniformLocation(Program, location), 1, false, transform);
                GLHelpers.CheckErrors();
            }
        }

        public override void SetUniform(string location, int v) {
            GL.Uniform1(GetUniformLocation((uint)Program, location), v);
            GLHelpers.CheckErrors();
        }

        public override void SetUniform(string location, Vector2 vec) {
            GL.Uniform2(GetUniformLocation((uint)Program, location), vec);
            GLHelpers.CheckErrors();
        }

        public override void SetUniform(string location, Vector3 vec) {
            GL.Uniform3(GetUniformLocation((uint)Program, location), vec);
            GLHelpers.CheckErrors();
        }


        public override void SetUniform(string location, Vector3[] vecs) {
            fixed (float* v = &vecs[0].X) {
                GL.Uniform3(GetUniformLocation((uint)Program, location), 3, v);
                GLHelpers.CheckErrors();
            }
        }

        public override void SetUniform(string location, Vector4 vec) {
            GL.Uniform4(GetUniformLocation((uint)Program, location), vec);
            GLHelpers.CheckErrors();
        }

        public override void SetUniform(string location, float v) {
            GL.Uniform1(GetUniformLocation((uint)Program, location), v);
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
                var infoLog = GL.GetProgramInfoLog(Program);
                _log.LogError($"Error: shader program compilation failed: {infoLog}");
                return;
            }
            else {
                _log.LogInformation($"{(Program != 0 ? "Reloaded" : "Loaded")} shader: {Name}");
            }

            GL.DeleteShader(vertexShader);
            GLHelpers.CheckErrors();
            GL.DeleteShader(fragmentShader);
            GLHelpers.CheckErrors();

            if (Program != 0) {
                Unload();
            }
            _uniformLocations.Clear();

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
                GLHelpers.CheckErrors();
                Program = 0;
            }
        }
    }
}