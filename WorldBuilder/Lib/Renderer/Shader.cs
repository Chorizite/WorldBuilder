using System;
using System.IO;
using System.Collections.Generic;
using Evergine.Bindings.OpenGL;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using System.Numerics;

namespace WorldBuilder.Lib.Renderer {
    public unsafe class GLSLShader : AShader {
        private FileSystemWatcher _watcher;
        private string _liveShaderDirectory;

        private string VertShaderName => $"{Name}.vert";
        private string FragShaderName => $"{Name}.frag";

        public GLSLShader(string name) : base(name) {
            VertShaderSource = File.ReadAllText(Path.Combine("Assets", "Shaders", VertShaderName));
            FragShaderSource = File.ReadAllText(Path.Combine("Assets", "Shaders", FragShaderName));
            /*
            _liveShaderDirectory = $"./../../../../ACInspector.OpenGLSDLBackend/Shaders/";
            if (Directory.Exists(_liveShaderDirectory)) {
                WatchShaderFiles(_liveShaderDirectory);
            }
            */

            LoadShader(VertShaderSource, FragShaderSource);
        }

        private void WatchShaderFiles(string shaderDir) {
            System.Diagnostics.Debug.WriteLine($"Watching {shaderDir}{Name}.*");
            _watcher = new FileSystemWatcher(shaderDir);
            _watcher.NotifyFilter = NotifyFilters.LastWrite;
            _watcher.Filter = $"*.*";
            _watcher.Changed += _watcher_Changed;
            _watcher.EnableRaisingEvents = true;
        }

        private void _watcher_Changed(object sender, FileSystemEventArgs e) {
            if (e.ChangeType == WatcherChangeTypes.Changed) {
                VertShaderSource = null;
                FragShaderSource = null;
                Reload();
            }
        }

        public override void Reload(string vertexSource = null, string fragmentSource = null) {
            try {
                base.Reload(vertexSource, fragmentSource);
            }
            catch { }
        }

        public override void SetActive() {
            if (NeedsLoad) {
                try {
                    VertShaderSource ??= File.ReadAllText(Path.Combine(_liveShaderDirectory, VertShaderName));
                    FragShaderSource ??= File.ReadAllText(Path.Combine(_liveShaderDirectory, FragShaderName));

                    LoadShader(VertShaderSource, FragShaderSource);
                }
                catch (Exception ex) {
                    //ACInspector.Lib.AppContext.Instance?.LogTool?.WriteLine(ex.ToString());
                }
            }
            GL.glUseProgram((uint)Program);
        }

        public override void SetUniform(string location, Matrix4x4 m) {
            IntPtr cName = IntPtr.Zero;
            try {
                var m2 = new float[] {
                    m.M11, m.M12, m.M13, m.M14,
                    m.M21, m.M22, m.M23, m.M24,
                    m.M31, m.M32, m.M33, m.M34,
                    m.M41, m.M42, m.M43, m.M44
                };
                cName = Marshal.StringToHGlobalAnsi(location);
                fixed (float* transform = m2) {
                    GL.glUniformMatrix4fv(GL.glGetUniformLocation((uint)Program, (char*)cName), 1, false, transform);
                }
            }
            finally {
                if (cName != IntPtr.Zero) {
                    Marshal.FreeHGlobal(cName);
                }
            }
        }

        public override void SetUniform(string location, int v) {
            IntPtr cName = IntPtr.Zero;
            try {
                cName = Marshal.StringToHGlobalAnsi(location);
                GL.glUniform1i(GL.glGetUniformLocation((uint)Program, (char*)cName), v);
            }
            finally {
                if (cName != IntPtr.Zero) {
                    Marshal.FreeHGlobal(cName);
                }
            }
        }

        public override void SetUniform(string location, Vector2 vec) {
            IntPtr cName = IntPtr.Zero;
            try {
                cName = Marshal.StringToHGlobalAnsi(location);
                GL.glUniform2f(GL.glGetUniformLocation((uint)Program, (char*)cName), vec.X, vec.Y);
            }
            finally {
                if (cName != IntPtr.Zero) {
                    Marshal.FreeHGlobal(cName);
                }
            }
        }

        public override void SetUniform(string location, Vector3 vec) {
            IntPtr cName = IntPtr.Zero;
            try {
                cName = Marshal.StringToHGlobalAnsi(location);
                GL.glUniform3f(GL.glGetUniformLocation((uint)Program, (char*)cName), vec.X, vec.Y, vec.Z);
            }
            finally {
                if (cName != IntPtr.Zero) {
                    Marshal.FreeHGlobal(cName);
                }
            }
        }


        public override void SetUniform(string location, Vector3[] vecs) {
            for (var i = 0; i < vecs.Length; i++) {
                var vec = vecs[i];
                IntPtr cName = IntPtr.Zero;
                try {
                    cName = Marshal.StringToHGlobalAnsi($"{location}[{i}]");
                    GL.glUniform3f(GL.glGetUniformLocation((uint)Program, (char*)cName), vec.X, vec.Y, vec.Z);
                }
                finally {
                    if (cName != IntPtr.Zero) {
                        Marshal.FreeHGlobal(cName);
                    }
                }
            }
        }

        public override void SetUniform(string location, Vector4 vec) {
            IntPtr cName = IntPtr.Zero;
            try {
                cName = Marshal.StringToHGlobalAnsi(location);
                GL.glUniform4f(GL.glGetUniformLocation((uint)Program, (char*)cName), vec.X, vec.Y, vec.Z, vec.W);
            }
            finally {
                if (cName != IntPtr.Zero) {
                    Marshal.FreeHGlobal(cName);
                }
            }
        }

        public override void SetUniform(string location, float v) {
            IntPtr cName = IntPtr.Zero;
            try {
                cName = Marshal.StringToHGlobalAnsi(location);
                GL.glUniform1f(GL.glGetUniformLocation((uint)Program, (char*)cName), v);
            }
            finally {
                if (cName != IntPtr.Zero) {
                    Marshal.FreeHGlobal(cName);
                }
            }
        }

        public override void SetUniform(string location, float[] vs) {
            for (var i = 0; i < vs.Length; i++) {
                var v = vs[i];
                IntPtr cName = IntPtr.Zero;
                try {
                    cName = Marshal.StringToHGlobalAnsi($"{location}[{i}]");
                    GL.glUniform1f(GL.glGetUniformLocation((uint)Program, (char*)cName), v);
                }
                finally {
                    if (cName != IntPtr.Zero) {
                        Marshal.FreeHGlobal(cName);
                    }
                }
            }
        }

        private void LoadShader(string vertShaderSource, string fragShaderSource) {
            NeedsLoad = false;

            uint vertexShader = CompileShader(ShaderType.VertexShader, Name, vertShaderSource);
            uint fragmentShader = CompileShader(ShaderType.FragmentShader, Name, fragShaderSource);

            var prog = GL.glCreateProgram();
            GL.glAttachShader(prog, vertexShader);
            GL.glAttachShader(prog, fragmentShader);
            GL.glLinkProgram(prog);

            int success = 0;
            GL.glGetProgramiv(prog, ProgramPropertyARB.LinkStatus, &success);
            if (success != 1) {
                var infoLog = stackalloc char[1024];
                GL.glGetProgramInfoLog(prog, 1024, (int*)0, infoLog);
                Console.WriteLine($"Error: shader program compilation failed: {Marshal.PtrToStringUTF8((IntPtr)infoLog)}");
                return;
            }
            else {
                Console.WriteLine($"{(Program != 0 ? "Reloaded" : "Loaded")} shader: {Name}");
            }

            GL.glDeleteShader(vertexShader);
            GL.glDeleteShader(fragmentShader);

            if (Program != 0) {
                Unload();
            }

            Program = prog;
        }

        private uint CompileShader(ShaderType shaderType, string name, string shaderSource) {
            uint shader = GL.glCreateShader(shaderType);

            IntPtr* textPtr = stackalloc IntPtr[1];
            textPtr[0] = Marshal.StringToHGlobalAnsi(shaderSource);
            int shaderSourceLength = shaderSource.Length;

            GL.glShaderSource(shader, 1, (IntPtr)textPtr, &shaderSourceLength);
            GL.glCompileShader(shader);

            int success = 0;
            var infoLog = stackalloc char[1024];
            GL.glGetShaderiv(shader, ShaderParameterName.CompileStatus, &success);
            if (success != 1) {
                GL.glGetShaderInfoLog(shader, 1024, (int*)0, infoLog);
                Console.WriteLine($"Error: {name}:{shaderType} compilation failed: {Marshal.PtrToStringUTF8((IntPtr)infoLog)}");
            }

            return shader;
        }

        private void Unload() {
            if (Program != 0) {
                GL.glDeleteProgram((uint)Program);
                Program = 0;
            }
        }

        public override void Dispose() {
            Unload();
            _watcher.Dispose();
            _watcher = null;
        }
    }
}
