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
using WorldBuilder.Lib.Settings;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Editors.Landscape {
    public class GameScene : IDisposable {
        private readonly DocumentManager _documentManager;
        private readonly OpenGLRenderer _renderer;
        private readonly WorldBuilderSettings _settings;
        private readonly GL _gl;
        private readonly IShader _terrainShader;
        private readonly IShader _sphereShader;
        internal readonly StaticObjectManager _objectManager;
        private readonly IDatReaderWriter _dats;

        // Sphere rendering resources (from TerrainRenderer)
        private uint _sphereVAO;
        private uint _sphereVBO;
        private uint _sphereIBO;
        private uint _sphereInstanceVBO;
        private int _sphereIndexCount;

        private bool _disposed = false;
        private float _aspectRatio;

        // Rendering properties (from TerrainRenderer)
        public float AmbientLightIntensity {
            get => _settings.Landscape.Rendering.LightIntensity;
            set => _settings.Landscape.Rendering.LightIntensity = value;
        }
        public bool ShowGrid {
            get => _settings.Landscape.Grid.ShowGrid;
            set => _settings.Landscape.Grid.ShowGrid = value;
        }
        public Vector3 LandblockGridColor {
            get => _settings.Landscape.Grid.LandblockColor;
            set => _settings.Landscape.Grid.LandblockColor = value;
        }
        public Vector3 CellGridColor {
            get => _settings.Landscape.Grid.CellColor;
            set => _settings.Landscape.Grid.CellColor = value;
        }
        public float GridLineWidth {
            get => _settings.Landscape.Grid.LineWidth;
            set => _settings.Landscape.Grid.LineWidth = value;
        }
        public float GridOpacity {
            get => _settings.Landscape.Grid.Opacity;
            set => _settings.Landscape.Grid.Opacity = value;
        }
        public Vector3 SphereColor {
            get => _settings.Landscape.Selection.SphereColor;
            set => _settings.Landscape.Selection.SphereColor = value;
        }
        public float SphereRadius {
            get => _settings.Landscape.Selection.SphereRadius;
            set => _settings.Landscape.Selection.SphereRadius = value;
        }
        public float SphereHeightOffset { get; set; } = 0.0f;
        public Vector3 LightDirection { get; set; } = new Vector3(0.5f, 0.3f, -0.3f);
        public float SpecularPower { get; set; } = 32.0f;
        public Vector3 SphereGlowColor { get; set; } = new(0);
        public float SphereGlowIntensity { get; set; } = 1.0f;
        public float SphereGlowPower { get; set; } = 0.5f;

        public GameScene(OpenGLRenderer renderer, WorldBuilderSettings settings, IDatReaderWriter dats, DocumentManager documentManager) {
            _documentManager = documentManager ?? throw new ArgumentNullException(nameof(documentManager));
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _dats = dats ?? throw new ArgumentNullException(nameof(dats));
            _gl = renderer.GraphicsDevice.GL;
            _objectManager = new StaticObjectManager(renderer, dats);

            // Initialize shaders
            var assembly = typeof(OpenGLRenderer).Assembly;
            _terrainShader = _renderer.GraphicsDevice.CreateShader("Landscape",
                GetEmbeddedResource("Chorizite.OpenGLSDLBackend.Shaders.Landscape.vert", assembly),
                GetEmbeddedResource("Chorizite.OpenGLSDLBackend.Shaders.Landscape.frag", assembly));
            _sphereShader = _renderer.GraphicsDevice.CreateShader("Sphere",
                GetEmbeddedResource("WorldBuilder.Shaders.Sphere.vert", typeof(GameScene).Assembly),
                GetEmbeddedResource("WorldBuilder.Shaders.Sphere.frag", typeof(GameScene).Assembly));

            InitializeSphereGeometry();
        }
        public static string GetEmbeddedResource(string filename, Assembly assembly) {
            using (Stream stream = assembly.GetManifestResourceStream(filename))
            using (StreamReader reader = new StreamReader(stream)) {
                string result = reader.ReadToEnd();
                return result;
            }
        }

        private unsafe void InitializeSphereGeometry() {
            var vertices = CreateSphere(8, 6);
            var indices = CreateSphereIndices(8, 6);
            _sphereIndexCount = indices.Length;

            _gl.GenVertexArrays(1, out _sphereVAO);
            _gl.BindVertexArray(_sphereVAO);

            _gl.GenBuffers(1, out _sphereVBO);
            _gl.BindBuffer(GLEnum.ArrayBuffer, _sphereVBO);
            fixed (VertexPositionNormal* ptr = vertices) {
                _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(vertices.Length * VertexPositionNormal.Size), ptr, GLEnum.StaticDraw);
            }

            int stride = VertexPositionNormal.Size;
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 3, GLEnum.Float, false, (uint)stride, (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 3, GLEnum.Float, false, (uint)stride, (void*)(3 * sizeof(float)));

            _gl.GenBuffers(1, out _sphereInstanceVBO);
            _gl.BindBuffer(GLEnum.ArrayBuffer, _sphereInstanceVBO);
            _gl.BufferData(GLEnum.ArrayBuffer, 0, null, GLEnum.DynamicDraw);
            _gl.EnableVertexAttribArray(2);
            _gl.VertexAttribPointer(2, 3, GLEnum.Float, false, (uint)sizeof(Vector3), null);
            _gl.VertexAttribDivisor(2, 1);

            _gl.GenBuffers(1, out _sphereIBO);
            _gl.BindBuffer(GLEnum.ElementArrayBuffer, _sphereIBO);
            fixed (uint* iptr = indices) {
                _gl.BufferData(GLEnum.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), iptr, GLEnum.StaticDraw);
            }

            _gl.BindVertexArray(0);
        }

        private VertexPositionNormal[] CreateSphere(int longitudeSegments, int latitudeSegments) {
            var vertices = new List<VertexPositionNormal>();
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
                    indices.Add(current);
                    indices.Add(next);
                    indices.Add(current + 1);
                    indices.Add(current + 1);
                    indices.Add(next);
                    indices.Add(next + 1);
                }
            }
            return indices.ToArray();
        }

        public void AddStaticObject(string landblockId, StaticObject obj) {
            var doc = _documentManager.GetOrCreateDocumentAsync(landblockId, typeof(LandblockDocument)).GetAwaiter().GetResult();
            if (doc is LandblockDocument lbDoc) {
                lbDoc.Apply(new StaticObjectUpdateEvent(obj, true));
            }
        }

        public void RemoveStaticObject(string landblockId, StaticObject obj) {
            var doc = _documentManager.GetOrCreateDocumentAsync(landblockId, typeof(LandblockDocument)).GetAwaiter().GetResult();
            if (doc is LandblockDocument lbDoc) {
                lbDoc.Apply(new StaticObjectUpdateEvent(obj, false));
            }
        }

        public void Render(
            ICamera camera,
            float aspectRatio,
            TerrainSystem terrainSystem,
            TerrainEditingContext editingContext,
            float width,
            float height) {
            _aspectRatio = aspectRatio;
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Less);
            _gl.DepthMask(true);
            _gl.ClearColor(0.2f, 0.3f, 0.8f, 1.0f);
            _gl.ClearDepth(1f);
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            _gl.Enable(EnableCap.CullFace);
            _gl.CullFace(TriangleFace.Back);

            Matrix4x4 model = Matrix4x4.Identity;
            Matrix4x4 view = camera.GetViewMatrix();
            Matrix4x4 projection = camera.GetProjectionMatrix();
            Matrix4x4 viewProjection = view * projection;

            float cameraDistance = MathF.Abs(camera.Position.Z);
            if (camera is OrthographicTopDownCamera orthoCamera) {
                cameraDistance = orthoCamera.OrthographicSize;
            }

            var frustum = new Frustum(viewProjection);
            var renderableChunks = terrainSystem.GetRenderableChunks(frustum);

            // Render terrain
            RenderTerrain(renderableChunks, terrainSystem.SurfaceManager, model, camera, cameraDistance, width, height);

            // Render active vertex spheres
            if (editingContext.ActiveVertices.Count > 0) {
                _gl.Disable(EnableCap.DepthTest);
                RenderActiveSpheres(editingContext, terrainSystem.DataManager, camera, model, viewProjection);
            }

            // Render static objects
            var staticObjects = terrainSystem.GetAllStaticObjects().ToList();
            if (staticObjects.Count > 0) {
                RenderStaticObjects(staticObjects, camera, viewProjection);
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
            _terrainShader.SetUniform("xAmbient", AmbientLightIntensity);
            _terrainShader.SetUniform("xWorld", model);
            _terrainShader.SetUniform("xView", camera.GetViewMatrix());
            _terrainShader.SetUniform("xProjection", camera.GetProjectionMatrix());
            _terrainShader.SetUniform("uAlpha", 1f);
            _terrainShader.SetUniform("uShowLandblockGrid", ShowGrid ? 1 : 0);
            _terrainShader.SetUniform("uShowCellGrid", ShowGrid ? 1 : 0);
            _terrainShader.SetUniform("uLandblockGridColor", LandblockGridColor);
            _terrainShader.SetUniform("uCellGridColor", CellGridColor);
            _terrainShader.SetUniform("uGridLineWidth", GridLineWidth);
            _terrainShader.SetUniform("uGridOpacity", GridOpacity);
            _terrainShader.SetUniform("uCameraDistance", cameraDistance);
            _terrainShader.SetUniform("uScreenHeight", height);

            surfaceManager.TerrainAtlas.Bind(0);
            _terrainShader.SetUniform("xOverlays", 0);
            surfaceManager.AlphaAtlas.Bind(1);
            _terrainShader.SetUniform("xAlphas", 1);

            foreach (var (chunk, renderData) in renderableChunks) {
                renderData.ArrayBuffer.Bind();
                renderData.VertexBuffer.Bind();
                renderData.IndexBuffer.Bind();
                GLHelpers.CheckErrors();
                _renderer.GraphicsDevice.DrawElements(Chorizite.Core.Render.Enums.PrimitiveType.TriangleList, renderData.TotalIndexCount);
                renderData.ArrayBuffer.Unbind();
                renderData.VertexBuffer.Unbind();
                renderData.IndexBuffer.Unbind();
            }
        }

        private unsafe void RenderActiveSpheres(
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

            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

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

            _gl.BindBuffer(GLEnum.ArrayBuffer, _sphereInstanceVBO);
            fixed (Vector3* posPtr = positions) {
                _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(count * sizeof(Vector3)), posPtr, GLEnum.DynamicDraw);
            }

            _gl.BindVertexArray(_sphereVAO);
            _gl.DrawElementsInstanced(GLEnum.Triangles, (uint)_sphereIndexCount, GLEnum.UnsignedInt, null, (uint)count);
            _gl.BindVertexArray(0);
            _gl.UseProgram(0);
            _gl.Disable(EnableCap.Blend);
        }


        private unsafe void RenderStaticObjects(List<StaticObject> objects, ICamera camera, Matrix4x4 viewProjection) {
            _gl.Enable(EnableCap.DepthTest);
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            _objectManager._objectShader.Bind();
            _objectManager._objectShader.SetUniform("uViewProjection", viewProjection);
            _objectManager._objectShader.SetUniform("uCameraPosition", camera.Position);
            _objectManager._objectShader.SetUniform("uLightDirection", Vector3.Normalize(LightDirection));
            _objectManager._objectShader.SetUniform("uAmbientIntensity", AmbientLightIntensity);
            _objectManager._objectShader.SetUniform("uSpecularPower", SpecularPower);

            // Group objects by (Id, IsSetup)
            var groups = objects.GroupBy(o => (Id: o.Id, IsSetup: o.IsSetup))
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(o => (
                        Transform: Matrix4x4.CreateFromQuaternion(o.Orientation) * Matrix4x4.CreateTranslation(o.Origin),
                        Object: o
                    )).ToList()
                );

            foreach (var group in groups) {
                var (id, isSetup) = group.Key;
                var renderData = _objectManager.GetRenderData(id, isSetup);

                if (renderData == null) {
                    Console.WriteLine($"Warning: No render data for object 0x{id:X8} (IsSetup={isSetup})");
                    continue;
                }

                if (isSetup) {
                    // Setup objects - render each part
                    foreach (var (partId, partTransform) in renderData.SetupParts) {
                        var partRenderData = _objectManager.GetRenderData(partId, false);
                        if (partRenderData == null) continue;

                        // Create instance data for each instance of this setup
                        var instanceData = group.Value.Select(inst =>
                            partTransform * inst.Transform
                        ).ToList();

                        RenderBatchedObject(partRenderData, instanceData);
                    }
                }
                else {
                    // Simple GfxObj - render directly
                    var instanceData = group.Value.Select(inst => inst.Transform).ToList();
                    RenderBatchedObject(renderData, instanceData);
                }
            }

            _gl.BindVertexArray(0);
            _gl.UseProgram(0);
            _gl.Disable(EnableCap.Blend);
        }

        private unsafe void RenderBatchedObject(StaticObjectRenderData renderData, List<Matrix4x4> instanceTransforms) {
            if (instanceTransforms.Count == 0 || renderData.Batches.Count == 0) return;

            // Create instance buffer - 16 floats per Matrix4x4
            uint instanceVBO;
            _gl.GenBuffers(1, out instanceVBO);
            _gl.BindBuffer(GLEnum.ArrayBuffer, instanceVBO);

            var instanceBuffer = new float[instanceTransforms.Count * 16];
            for (int i = 0; i < instanceTransforms.Count; i++) {
                var transform = instanceTransforms[i];
                float[] matrixData = new float[16] {
                    transform.M11, transform.M12, transform.M13, transform.M14,
                    transform.M21, transform.M22, transform.M23, transform.M24,
                    transform.M31, transform.M32, transform.M33, transform.M34,
                    transform.M41, transform.M42, transform.M43, transform.M44
                };
                Array.Copy(matrixData, 0, instanceBuffer, i * 16, 16);
            }

            fixed (float* ptr = instanceBuffer) {
                _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(instanceBuffer.Length * sizeof(float)), ptr, GLEnum.DynamicDraw);
            }

            _gl.BindVertexArray(renderData.VAO);

            // Set up instance attributes (mat4 takes 4 attribute slots)
            for (int i = 0; i < 4; i++) {
                _gl.EnableVertexAttribArray((uint)(3 + i));
                _gl.VertexAttribPointer((uint)(3 + i), 4, GLEnum.Float, false, (uint)(16 * sizeof(float)), (void*)(i * 4 * sizeof(float)));
                _gl.VertexAttribDivisor((uint)(3 + i), 1);
            }

            // Render each batch with its texture
            foreach (var batch in renderData.Batches) {
                if (batch.TextureArray == null) {
                    Console.WriteLine($"Warning: Batch has null texture array");
                    continue;
                }

                try {
                    // Bind the texture array for this batch
                    batch.TextureArray.Bind(0);
                    _objectManager._objectShader.SetUniform("uTextureArray", 0);

                    // Set the texture layer index
                    // The shader should sample from this layer: texture(uTextureArray, vec3(uv, uTextureIndex))
                    _objectManager._objectShader.SetUniform("uTextureIndex", (float)batch.TextureIndex);

                    // Bind the index buffer for this batch
                    _gl.BindBuffer(GLEnum.ElementArrayBuffer, batch.IBO);

                    // Draw all instances with this batch
                    // IMPORTANT: Using UnsignedShort because we're using ushort indices
                    _gl.DrawElementsInstanced(GLEnum.Triangles, (uint)batch.IndexCount, GLEnum.UnsignedShort, null, (uint)instanceTransforms.Count);
                }
                catch (Exception ex) {
                    Console.WriteLine($"Error rendering batch (texture index {batch.TextureIndex}): {ex.Message}");
                }
            }

            _gl.BindVertexArray(0);
            _gl.DeleteBuffer(instanceVBO);
        }

        public void Dispose() {
            if (!_disposed) {
                _gl.DeleteBuffer(_sphereVBO);
                _gl.DeleteBuffer(_sphereIBO);
                _gl.DeleteBuffer(_sphereInstanceVBO);
                _gl.DeleteVertexArray(_sphereVAO);
                _objectManager?.Dispose();
                _disposed = true;
            }
        }
    }
}