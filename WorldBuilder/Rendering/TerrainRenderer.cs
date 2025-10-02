
// ===== Core Data Structures =====

using Chorizite.Core.Render;
using Chorizite.Core.Render.Vertex;
using Chorizite.OpenGLSDLBackend;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using WorldBuilder.Lib;
using WorldBuilder.Tools.Landscape;

namespace WorldBuilder.Test {
    // ===== Updated Renderer =====

    public unsafe class TerrainRenderer : IDisposable {
        private readonly IRenderer _render;
        private readonly GL gl;

        // Shader resources
        private IShader _terrainShader;
        private IShader _sphereShader;

        // Sphere rendering resources
        private uint _sphereVAO;
        private uint _sphereVBO;
        private uint _sphereIBO;
        private uint _sphereInstanceVBO;
        private int _sphereIndexCount;

        private bool _disposed = false;
        private float _aspectRatio;

        // Rendering properties
        public float AmbientLightIntensity { get; set; } = 0.45f;
        public bool ShowGrid { get; set; } = true;
        public Vector3 LandblockGridColor { get; set; } = new Vector3(1.0f, 0f, 1.0f);
        public Vector3 CellGridColor { get; set; } = new Vector3(0f, 1f, 1f);
        public float GridLineWidth { get; set; } = 1f;
        public float GridOpacity { get; set; } = .40f;

        // Sphere properties
        public Vector3 SphereColor { get; set; } = new Vector3(1.0f, 1.0f, 1.0f);
        public float SphereRadius { get; set; } = 4.6f;
        public float SphereHeightOffset { get; set; } = 0.0f;
        public Vector3 LightDirection { get; set; } = new Vector3(0.5f, 0.3f, -0.3f);
        public float SpecularPower { get; set; } = 32.0f;
        public Vector3 SphereGlowColor { get; set; } = new(0);
        public float SphereGlowIntensity { get; set; } = 1.0f;
        public float SphereGlowPower { get; set; } = 0.5f;

        public TerrainRenderer(IRenderer render) {
            _render = render;
            this.gl = (render as OpenGLRenderer).GraphicsDevice.GL;
            InitializeShaders();
            InitializeSphereGeometry();
        }

        private void InitializeShaders() {
            // Use current implementation from old TerrainRenderer
            var a1 = typeof(OpenGLRenderer).Assembly;
            _terrainShader = _render.GraphicsDevice.CreateShader("Landscape",
                TerrainRenderer.GetEmbeddedResource("Shaders.Landscape.vert", a1),
                TerrainRenderer.GetEmbeddedResource("Shaders.Landscape.frag", a1));
            _terrainShader.Bind();
            _sphereShader = _render.GraphicsDevice.CreateShader("Sphere",
                TerrainRenderer.GetEmbeddedResource("Shaders.Sphere.vert"),
                TerrainRenderer.GetEmbeddedResource("Shaders.Sphere.frag"));
        }

        private void InitializeSphereGeometry() {
            // Create a sphere mesh (icosphere or UV sphere)
            var vertices = CreateSphere(8, 6);
            var indices = CreateSphereIndices(8, 6);
            _sphereIndexCount = indices.Length;

            gl.GenVertexArrays(1, out _sphereVAO);
            gl.BindVertexArray(_sphereVAO);

            gl.GenBuffers(1, out _sphereVBO);
            gl.BindBuffer(GLEnum.ArrayBuffer, _sphereVBO);
            fixed (VertexPositionNormal* ptr = vertices) {
                gl.BufferData(GLEnum.ArrayBuffer, (nuint)(vertices.Length * VertexPositionNormal.Size), ptr, GLEnum.StaticDraw);
            }

            int stride = VertexPositionNormal.Size;
            // Position (location 0)
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 3, GLEnum.Float, false, (uint)stride, (void*)0);
            // Normal (location 1)
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 3, GLEnum.Float, false, (uint)stride, (void*)(3 * sizeof(float)));

            // Instance buffer (initially empty, dynamic)
            gl.GenBuffers(1, out _sphereInstanceVBO);
            gl.BindBuffer(GLEnum.ArrayBuffer, _sphereInstanceVBO);
            gl.BufferData(GLEnum.ArrayBuffer, 0, null, GLEnum.DynamicDraw);
            // Instance position (location 2)
            gl.EnableVertexAttribArray(2);
            gl.VertexAttribPointer(2, 3, GLEnum.Float, false, (uint)sizeof(Vector3), null);
            gl.VertexAttribDivisor(2, 1);

            gl.GenBuffers(1, out _sphereIBO);
            gl.BindBuffer(GLEnum.ElementArrayBuffer, _sphereIBO);
            fixed (uint* iptr = indices) {
                gl.BufferData(GLEnum.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), iptr, GLEnum.StaticDraw);
            }

            gl.BindVertexArray(0);
        }

        private VertexPositionNormal[] CreateSphere(int longitudeSegments, int latitudeSegments) {
            var vertices = new List<VertexPositionNormal>();

            // Generate vertices
            for (int lat = 0; lat <= latitudeSegments; lat++) {
                float theta = lat * MathF.PI / latitudeSegments;
                float sinTheta = MathF.Sin(theta);
                float cosTheta = MathF.Cos(theta);

                for (int lon = 0; lon <= longitudeSegments; lon++) {
                    float phi = lon * 2 * MathF.PI / longitudeSegments;
                    float sinPhi = MathF.Sin(phi);
                    float cosPhi = MathF.Cos(phi);

                    float x = cosPhi * sinTheta;
                    float y = cosTheta;
                    float z = sinPhi * sinTheta;

                    Vector3 position = new Vector3(x, y, z);
                    Vector3 normal = Vector3.Normalize(position);

                    vertices.Add(new VertexPositionNormal(position, normal));
                }
            }

            return vertices.ToArray();
        }

        private uint[] CreateSphereIndices(int longitudeSegments, int latitudeSegments) {
            var indices = new List<uint>();

            for (int lat = 0; lat < latitudeSegments; lat++) {
                for (int lon = 0; lon < longitudeSegments; lon++) {
                    uint current = (uint)(lat * (longitudeSegments + 1) + lon);
                    uint next = current + (uint)(longitudeSegments + 1);

                    // First triangle
                    indices.Add(current);
                    indices.Add(next);
                    indices.Add(current + 1);

                    // Second triangle
                    indices.Add(current + 1);
                    indices.Add(next);
                    indices.Add(next + 1);
                }
            }

            return indices.ToArray();
        }

        internal static string GetEmbeddedResource(string filename, Assembly? ass = null) {
            var assembly = ass ?? typeof(TerrainRenderer).Assembly;
            var resourceName = "WorldBuilder." + filename;

            if (ass is not null) {
                resourceName = "Chorizite.OpenGLSDLBackend." + filename;
            }

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            using (StreamReader reader = new StreamReader(stream)) {
                string result = reader.ReadToEnd();
                return result;
            }
        }

        /// <summary>
        /// Main render method - now takes terrain system and editing context
        /// </summary>
        public void Render(
            ICamera camera,
            float aspectRatio,
            TerrainSystem terrainSystem,
            TerrainEditingContext editingContext,
            float width,
            float height) {

            _aspectRatio = aspectRatio;

            // Clear screen
            gl.Enable(EnableCap.DepthTest);
            gl.DepthFunc(DepthFunction.Less);
            gl.DepthMask(true);
            gl.ClearColor(0.2f, 0.3f, 0.8f, 1.0f);
            gl.ClearDepth(1f);
            gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            gl.Enable(EnableCap.CullFace);
            gl.CullFace(TriangleFace.Back);

            // Calculate matrices
            Matrix4x4 model = Matrix4x4.Identity;
            Matrix4x4 view = camera.GetViewMatrix();
            Matrix4x4 projection = camera.GetProjectionMatrix(aspectRatio, 1f, 10000f);
            Matrix4x4 viewProjection = view * projection;

            float cameraDistance = MathF.Abs(camera.Position.Z);
            if (camera is OrthographicTopDownCamera orthoCamera) {
                cameraDistance = orthoCamera.OrthographicSize;
            }

            // Get renderable chunks
            var frustum = new Frustum(viewProjection);
            var renderableChunks = terrainSystem.GetRenderableChunks(frustum);

            // Render terrain
            RenderTerrain(renderableChunks, terrainSystem.SurfaceManager, model, camera, cameraDistance, width, height);

            // Render active vertex spheres
            if (editingContext.ActiveVertices.Count > 0) {
                gl.Disable(EnableCap.DepthTest);
                RenderActiveSpheres(editingContext, terrainSystem.DataManager, camera, model, viewProjection);
            }
        }

        private void RenderTerrain(
            IEnumerable<(TerrainChunk chunk, ChunkRenderData renderData)> renderableChunks,
            LandSurfaceManager surfaceManager,
            Matrix4x4 model,
            ICamera camera,
            float cameraDistance,
            float width,
            float height) {

            _terrainShader.Bind();

            // Set uniforms
            _terrainShader.SetUniform("xAmbient", AmbientLightIntensity);
            _terrainShader.SetUniform("xWorld", model);
            _terrainShader.SetUniform("xView", camera.GetViewMatrix());
            _terrainShader.SetUniform("xProjection", camera.GetProjectionMatrix(_aspectRatio, 1f, 192f * 255f * 2f));
            _terrainShader.SetUniform("uAlpha", 1f);
            _terrainShader.SetUniform("uShowLandblockGrid", ShowGrid ? 1 : 0);
            _terrainShader.SetUniform("uShowCellGrid", ShowGrid ? 1 : 0);
            _terrainShader.SetUniform("uLandblockGridColor", LandblockGridColor);
            _terrainShader.SetUniform("uCellGridColor", CellGridColor);
            _terrainShader.SetUniform("uGridLineWidth", GridLineWidth);
            _terrainShader.SetUniform("uGridOpacity", GridOpacity);
            _terrainShader.SetUniform("uCameraDistance", cameraDistance);
            _terrainShader.SetUniform("uScreenHeight", height);

            // Bind texture atlases
            surfaceManager.TerrainAtlas.Bind(0);
            _terrainShader.SetUniform("xOverlays", 0);
            surfaceManager.AlphaAtlas.Bind(1);
            _terrainShader.SetUniform("xAlphas", 1);

            // Render each chunk
            foreach (var (chunk, renderData) in renderableChunks) {
                renderData.VertexArray.Bind();
                renderData.VertexBuffer.Bind();
                renderData.IndexBuffer.Bind();

                _render.GraphicsDevice.DrawElements(Chorizite.Core.Render.Enums.PrimitiveType.TriangleList, renderData.IndexCount);

                renderData.VertexArray.Unbind();
                renderData.VertexBuffer.Unbind();
                renderData.IndexBuffer.Unbind();
            }
        }

        private void RenderActiveSpheres(
            TerrainEditingContext editingContext,
            TerrainDataManager dataManager,
            ICamera camera,
            Matrix4x4 model,
            Matrix4x4 viewProjection) {

            var activeVerts = editingContext.ActiveVertices.ToArray();
            if (activeVerts.Length == 0) return;

            int count = activeVerts.Length;
            var positions = new Vector3[count];
            for (int i = 0; i < count; i++) {
                try {
                    var vertex = activeVerts[i];
                    positions[i] = new Vector3(
                        vertex.X,
                        vertex.Y,
                        dataManager.GetHeightAtPosition(vertex.X, vertex.Y) + SphereHeightOffset);
                }
                catch {
                    positions[i] = Vector3.Zero;
                }
            }

            gl.Enable(EnableCap.Blend);
            gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            _sphereShader.Bind();
            _sphereShader.SetUniform("uViewProjection", viewProjection);
            _sphereShader.SetUniform("uCameraPosition", camera.Position);
            _sphereShader.SetUniform("uSphereColor", SphereColor);
            Vector3 normLight = Vector3.Normalize(LightDirection);
            _sphereShader.SetUniform("uLightDirection", normLight);
            _sphereShader.SetUniform("uAmbientIntensity", AmbientLightIntensity);
            _sphereShader.SetUniform("uSpecularPower", SpecularPower);
            _sphereShader.SetUniform("uGlowColor", SphereGlowColor);
            _sphereShader.SetUniform("uGlowIntensity", SphereGlowIntensity);
            _sphereShader.SetUniform("uGlowPower", SphereGlowPower);
            _sphereShader.SetUniform("uSphereRadius", SphereRadius);

            // Update instance buffer
            gl.BindBuffer(GLEnum.ArrayBuffer, _sphereInstanceVBO);
            fixed (Vector3* posPtr = positions) {
                gl.BufferData(GLEnum.ArrayBuffer, (nuint)(count * sizeof(Vector3)), posPtr, GLEnum.DynamicDraw);
            }

            gl.BindVertexArray(_sphereVAO);
            gl.DrawElementsInstanced(GLEnum.Triangles, (uint)_sphereIndexCount, GLEnum.UnsignedInt, null, (uint)count);
            gl.BindVertexArray(0);
            gl.UseProgram(0);
            gl.Disable(EnableCap.Blend);
        }

        public void Dispose() {
            if (!_disposed) {
                gl.DeleteBuffer(_sphereVBO);
                gl.DeleteBuffer(_sphereIBO);
                gl.DeleteBuffer(_sphereInstanceVBO);
                gl.DeleteVertexArray(_sphereVAO);
                _disposed = true;
            }
        }
    }
}