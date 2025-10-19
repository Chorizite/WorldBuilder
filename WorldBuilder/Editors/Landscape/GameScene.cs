using Chorizite.ACProtocol.Types;
using Chorizite.Core.Render;
using Chorizite.Core.Render.Vertex;
using Chorizite.OpenGLSDLBackend;
using DatReaderWriter.DBObjs;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using WorldBuilder.Lib;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Editors.Landscape {
    public class GameScene : IDisposable {
        private  float ProximityThreshold = 500f;  // 2D distance for loading

        private OpenGLRenderer _renderer => _terrainSystem.Renderer;
        private WorldBuilderSettings _settings => _terrainSystem.Settings;
        private GL _gl => _renderer.GraphicsDevice.GL;
        private IShader _terrainShader;
        private IShader _sphereShader;
        //internal readonly StaticObjectManager _objectManager;
        private IDatReaderWriter _dats => _terrainSystem.Dats;
        private DocumentManager _documentManager => _terrainSystem.DocumentManager;
        private TerrainDocument _terrainDoc => _terrainSystem.TerrainDoc;
        private Region _region => _terrainSystem.Region;

        public TerrainDataManager DataManager { get; }
        public LandSurfaceManager SurfaceManager { get; }
        public TerrainGPUResourceManager GPUManager { get; }

        public PerspectiveCamera PerspectiveCamera { get; private set; }
        public OrthographicTopDownCamera TopDownCamera { get; private set; }
        public CameraManager CameraManager { get; private set; }

        private readonly Dictionary<ushort, List<StaticObject>> _sceneryObjects = new();
        private readonly TerrainSystem _terrainSystem;

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

        public GameScene(TerrainSystem terrainSystem) {
            _terrainSystem = terrainSystem;
            var mapCenter = new Vector3(192f * 254f / 2f, 192f * 254f / 2f, 1000);
            PerspectiveCamera = new PerspectiveCamera(mapCenter, _settings);
            TopDownCamera = new OrthographicTopDownCamera(new Vector3(0, 0, 200f), _settings);
            CameraManager = new CameraManager(TopDownCamera);
            //CameraManager.AddCamera(PerspectiveCamera);

            DataManager = new TerrainDataManager(terrainSystem, 16);
            SurfaceManager = new LandSurfaceManager(_renderer, _dats, _region);
            GPUManager = new TerrainGPUResourceManager(_renderer);

            //_objectManager = new StaticObjectManager(renderer, dats);

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

        public void Update(Vector3 cameraPosition, Matrix4x4 viewProjectionMatrix) {
            var frustum = new Frustum(viewProjectionMatrix);
            var requiredChunks = DataManager.GetRequiredChunks(cameraPosition);
            //ProximityThreshold = 5000f;
            //UpdateDynamicDocumentsAsync(cameraPosition).GetAwaiter().GetResult();

            foreach (var chunkId in requiredChunks) {
                var chunkX = (uint)(chunkId >> 32);
                var chunkY = (uint)(chunkId & 0xFFFFFFFF);
                var chunk = DataManager.GetOrCreateChunk(chunkX, chunkY);

                if (!GPUManager.HasRenderData(chunkId)) {
                    GPUManager.CreateChunkResources(chunk, _terrainSystem);
                }
                else if (chunk.IsDirty) {
                    var dirtyLandblocks = chunk.DirtyLandblocks.ToList();
                    GPUManager.UpdateLandblocks(chunk, dirtyLandblocks, _terrainSystem);
                }
            }
        }

        private async Task UpdateDynamicDocumentsAsync(Vector3 cameraPosition) {
            return;
            /*
            var visibleLandblocks = GetProximateLandblocks(cameraPosition);
            var currentLoaded = _documentManager.ActiveDocs.Keys.Where(k => k.StartsWith("landblock_")).ToHashSet();

            // Load new ones in batch
            var toLoad = visibleLandblocks
                .Where(lbKey => !currentLoaded.Contains($"landblock_{lbKey:X4}"))
                .Select(lbKey => $"landblock_{lbKey:X4}")
                .ToList();

            if (toLoad.Any()) {
                var loadTasks = toLoad.Select(docId => _documentManager.GetOrCreateDocumentAsync<LandblockDocument>(docId)).ToArray();
                await Task.WhenAll(loadTasks);

                foreach (var docId in toLoad) {
                    var lbKey = ushort.Parse(docId.Replace("landblock_", ""), System.Globalization.NumberStyles.HexNumber);
                    var doc = _documentManager.GetOrCreateDocumentAsync<LandblockDocument>(docId).GetAwaiter().GetResult();
                    if (doc != null) {
                        var scenery = GenerateScenery(lbKey, doc);
                        _sceneryObjects[lbKey] = scenery;
                    }
                }
            }
            */
            // Unload out-of-range (sequential is fine, unloads are rare/light)
            /*
            var toUnload = currentLoaded
                .Where(docId => !visibleLandblocks.Contains(ushort.Parse(docId.Replace("landblock_", ""), System.Globalization.NumberStyles.HexNumber)))
                .ToList();

            foreach (var docId in toUnload) {
                var doc = _documentManager.GetOrCreateDocumentAsync<LandblockDocument>(docId).GetAwaiter().GetResult();
                if (doc != null) {
                    foreach (var obj in doc.GetStaticObjects()) {
                        _objectManager.ReleaseRenderData(obj.Id, obj.IsSetup);
                    }
                }

                var lbKey = ushort.Parse(docId.Replace("landblock_", ""), System.Globalization.NumberStyles.HexNumber);
                if (_sceneryObjects.TryGetValue(lbKey, out var scenery)) {
                    foreach (var obj in scenery) {
                        _objectManager.ReleaseRenderData(obj.Id, obj.IsSetup);
                    }
                    _sceneryObjects.Remove(lbKey);
                }

                await _documentManager.CloseDocumentAsync(docId);
            }
            */
        }

        private HashSet<ushort> GetProximateLandblocks(Vector3 cameraPosition) {
            var proximate = new HashSet<ushort>();
            var camLbX = (ushort)(cameraPosition.X / TerrainDataManager.LandblockLength);
            var camLbY = (ushort)(cameraPosition.Y / TerrainDataManager.LandblockLength);

            // Simple 2D grid search around camera (e.g., +/- 3 landblocks)
            var lbd = (int)Math.Ceiling(ProximityThreshold / 192f / 2f);
            for (int dx = -lbd; dx <= lbd; dx++) {
                for (int dy = -lbd; dy <= lbd; dy++) {
                    var lbX = (ushort)Math.Clamp(camLbX + dx, 0, TerrainDataManager.MapSize - 1);
                    var lbY = (ushort)Math.Clamp(camLbY + dy, 0, TerrainDataManager.MapSize - 1);
                    var lbKey = (ushort)((lbX << 8) | lbY);
                    var lbCenter = new Vector2(lbX * TerrainDataManager.LandblockLength + TerrainDataManager.LandblockLength / 2,
                                               lbY * TerrainDataManager.LandblockLength + TerrainDataManager.LandblockLength / 2);
                    var dist2D = Vector2.Distance(new Vector2(cameraPosition.X, cameraPosition.Y), lbCenter);
                    if (dist2D <= ProximityThreshold) {
                        proximate.Add(lbKey);
                    }
                }
            }
            return proximate;
        }

        public void RegenerateChunks(IEnumerable<ulong> chunkIds) {
            foreach (var chunkId in chunkIds) {
                var chunkX = (uint)(chunkId >> 32);
                var chunkY = (uint)(chunkId & 0xFFFFFFFF);
                var chunk = DataManager.GetOrCreateChunk(chunkX, chunkY);

                GPUManager.CreateChunkResources(chunk, _terrainSystem);
            }
        }

        public void UpdateLandblocks(IEnumerable<uint> landblockIds) {
            var landblocksByChunk = new Dictionary<ulong, List<uint>>();

            foreach (var landblockId in landblockIds) {
                var landblockX = landblockId >> 8;
                var landblockY = landblockId & 0xFF;
                var chunk = DataManager.GetChunkForLandblock(landblockX, landblockY);

                if (chunk == null) continue;

                var chunkId = chunk.GetChunkId();
                if (!landblocksByChunk.ContainsKey(chunkId)) {
                    landblocksByChunk[chunkId] = new List<uint>();
                }
                landblocksByChunk[chunkId].Add(landblockId);

                // Regenerate scenery for updated landblock if loaded
                /*
                var lbKey = (ushort)landblockId;
                if (_sceneryObjects.ContainsKey(lbKey)) {
                    var doc = _documentManager.GetOrCreateDocumentAsync<LandblockDocument>($"landblock_{lbKey:X4}").GetAwaiter().GetResult();
                    if (doc != null) {
                        // Release old scenery render data
                        foreach (var obj in _sceneryObjects[lbKey]) {
                            _objectManager.ReleaseRenderData(obj.Id, obj.IsSetup);
                        }
                        var newScenery = GenerateScenery(lbKey, doc);
                        _sceneryObjects[lbKey] = newScenery;
                    }
                }
                */
            }

            foreach (var kvp in landblocksByChunk) {
                var chunk = DataManager.GetChunk(kvp.Key);
                if (chunk != null) {
                    GPUManager.UpdateLandblocks(chunk, kvp.Value, _terrainSystem);
                }
            }
        }

        private List<StaticObject> GenerateScenery(ushort lbKey, LandblockDocument lbDoc) {
            return new();
            var scenery = new List<StaticObject>();
            var lbId = (uint)lbKey;
            var lbTerrainEntries = _terrainSystem.GetLandblockTerrain(lbKey);
            
            if (lbTerrainEntries == null) {
                return scenery;
            }

            var buildings = new HashSet<int>();
            var lbGlobalX = (lbId >> 8) & 0xFF;
            var lbGlobalY = lbId & 0xFF;
            
            // Build set of cells that contain buildings
            foreach (var b in lbDoc.GetStaticObjects()) {
                var localX = b.Origin.X - lbGlobalX * 192f;
                var localY = b.Origin.Y - lbGlobalY * 192f;
                var cellX = (int)MathF.Floor(localX / 24f);
                var cellY = (int)MathF.Floor(localY / 24f);
                
                if (cellX >= 0 && cellX < 8 && cellY >= 0 && cellY < 8) {
                    buildings.Add(cellX * 9 + cellY);
                }
            }

            var blockCellX = (int)lbGlobalX * 8;
            var blockCellY = (int)lbGlobalY * 8;

            for (int i = 0; i < lbTerrainEntries.Length; i++) {
                var entry = lbTerrainEntries[i];
                var terrainType = entry.Type;
                var sceneType = entry.Scenery;

                if (terrainType >= _region.TerrainInfo.TerrainTypes.Count) continue;
                
                var terrainInfo = _region.TerrainInfo.TerrainTypes[(int)terrainType];
                if (sceneType >= terrainInfo.SceneTypes.Count) continue;
                
                var sceneInfoIdx = terrainInfo.SceneTypes[(int)sceneType];
                var sceneInfo = _region.SceneInfo.SceneTypes[(int)sceneInfoIdx];
                
                if (sceneInfo.Scenes.Count == 0) {
                    continue;
                }

                var cellX = i / 9;
                var cellY = i % 9;
                var globalCellX = (uint)(blockCellX + cellX);
                var globalCellY = (uint)(blockCellY + cellY);

                // Scene selection
                var cellMat = globalCellY * (712977289u * globalCellX + 1813693831u) - 1109124029u * globalCellX + 2139937281u;
                var offset = cellMat * 2.3283064e-10f;
                var sceneIdx = (int)(sceneInfo.Scenes.Count * offset);
                sceneIdx = Math.Clamp(sceneIdx, 0, sceneInfo.Scenes.Count - 1);
                var sceneId = sceneInfo.Scenes[sceneIdx];

                if (!_dats.TryGet<Scene>(sceneId, out var scene) || scene.Objects.Count == 0) {
                    continue;
                }

                // Skip roads and buildings
                if (entry.Road != 0) {
                    continue;
                }
                if (buildings.Contains(i)) {
                    continue;
                }

                var cellXMat = -1109124029 * (int)globalCellX;
                var cellYMat = 1813693831 * (int)globalCellY;
                var cellMat2 = 1360117743 * globalCellX * globalCellY + 1888038839;

                for (uint j = 0; j < scene.Objects.Count; j++) {
                    var obj = scene.Objects[(int)j];
                    
                    if (obj.ObjectId == 0) {
                        continue;
                    }

                    var noise = (uint)(cellXMat + cellYMat - cellMat2 * (23399 + j)) * 2.3283064e-10f;
                    if (noise >= obj.Frequency) continue;  // spawn when noise < frequency

                    var localPos = SceneryHelpers.Displace(obj, globalCellX, globalCellY, j);
                    var lx = cellX * 24f + localPos.X;
                    var ly = cellY * 24f + localPos.Y;
                    
                    if (lx < 0 || ly < 0 || lx >= 192f || ly >= 192f) {
                        continue;
                    }

                    if (TerrainGeometryGenerator.OnRoad(new Vector3(lx, ly, 0), lbTerrainEntries)) {
                        continue;
                    }

                    var lbOffset = new Vector3(lx, ly, 0);

                    var z = TerrainGeometryGenerator.GetHeight(_region, lbTerrainEntries, lbGlobalX, lbGlobalY, lbOffset);
                    localPos.Z = z;
                    lbOffset.Z = z;

                    var normal = TerrainGeometryGenerator.GetNormal(_region, lbTerrainEntries, lbGlobalX, lbGlobalY, lbOffset);
                    
                    if (!SceneryHelpers.CheckSlope(obj, normal.Z)) {
                        continue;
                    }

                    Quaternion quat;
                    if (obj.Align != 0) {
                        quat = SceneryHelpers.ObjAlign(obj, normal, z, localPos);
                    }
                    else {
                        quat = SceneryHelpers.RotateObj(obj, globalCellX, globalCellY, j, localPos);
                    }

                    var scaleVal = SceneryHelpers.ScaleObj(obj, globalCellX, globalCellY, j);
                    var scale = new Vector3(scaleVal);

                    var blockX = (lbId >> 8) & 0xFF;
                    var blockY = lbId & 0xFF;
                    var worldOrigin = new Vector3(blockX * 192f + lx, blockY * 192f + ly, z);

                    scenery.Add(new StaticObject {
                        Id = obj.ObjectId,
                        Origin = worldOrigin,
                        Orientation = quat,
                        IsSetup = (obj.ObjectId & 0x02000000) != 0,
                        Scale = scale
                    });
                }
            }
            return scenery;
        }

        public IEnumerable<(TerrainChunk chunk, ChunkRenderData renderData)> GetRenderableChunks(Frustum frustum) {
            foreach (var chunk in DataManager.GetAllChunks()) {
                if (!frustum.IntersectsBoundingBox(chunk.Bounds)) continue;

                var renderData = GPUManager.GetRenderData(chunk.GetChunkId());
                if (renderData != null) {
                    yield return (chunk, renderData);
                }
            }
        }

        public int GetLoadedChunkCount() => DataManager.GetAllChunks().Count();
        public int GetVisibleChunkCount(Frustum frustum) => GetRenderableChunks(frustum).Count();

        public IEnumerable<StaticObject> GetAllStaticObjects() {
            return new List<StaticObject>();
            /*
            var statics = new List<StaticObject>();
            foreach (var doc in _documentManager.ActiveDocs.Values.OfType<LandblockDocument>()) {
                statics.AddRange(doc.GetStaticObjects());
            }
            statics.AddRange(_sceneryObjects.Values.SelectMany(x => x));
            return statics;
            */
        }

        public void Render(
            ICamera camera,
            float aspectRatio,
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
            var renderableChunks = GetRenderableChunks(frustum);

            // Render terrain
            RenderTerrain(renderableChunks, model, camera, cameraDistance, width, height);

            // Render active vertex spheres
            if (editingContext.ActiveVertices.Count > 0) {
                RenderActiveSpheres(editingContext, camera, model, viewProjection);
            }

            // Render static objects
            var staticObjects = GetAllStaticObjects().ToList();
            if (staticObjects.Count > 0) {
                RenderStaticObjects(staticObjects, camera, viewProjection);
            }
        }

        private void RenderTerrain(
            IEnumerable<(TerrainChunk chunk, ChunkRenderData renderData)> renderableChunks,
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

            SurfaceManager.TerrainAtlas.Bind(0);
            _terrainShader.SetUniform("xOverlays", 0);
            SurfaceManager.AlphaAtlas.Bind(1);
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
                        DataManager.GetHeightAtPosition(vertex.X, vertex.Y) + SphereHeightOffset);
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
            /*
            _gl.Enable(EnableCap.DepthTest);
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            _gl.Disable(EnableCap.CullFace);

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
                        Transform: Matrix4x4.CreateScale(o.Scale) * Matrix4x4.CreateFromQuaternion(o.Orientation) * Matrix4x4.CreateTranslation(o.Origin),
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
            */
        }

        private unsafe void RenderBatchedObject(StaticObjectRenderData renderData, List<Matrix4x4> instanceTransforms) {
            /*
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
                if (batch.TextureArray == null) continue;

                try {
                    // Bind the texture array for this batch
                    batch.TextureArray.Bind(0);
                    _objectManager._objectShader.SetUniform("uTextureArray", 0);

                    // Set the texture layer index
                    _objectManager._objectShader.SetUniform("uTextureIndex", (float)batch.TextureIndex);

                    // Bind the index buffer for this batch
                    _gl.BindBuffer(GLEnum.ElementArrayBuffer, batch.IBO);

                    // Draw all instances with this batch
                    _gl.DrawElementsInstanced(GLEnum.Triangles, (uint)batch.IndexCount, GLEnum.UnsignedShort, null, (uint)instanceTransforms.Count);
                }
                catch (Exception ex) {
                    Console.WriteLine($"Error rendering batch (texture index {batch.TextureIndex}): {ex.Message}");
                }
            }

            _gl.BindVertexArray(0);
            _gl.DeleteBuffer(instanceVBO);
            */
        }

        public void Dispose() {
            if (!_disposed) {
                _gl.DeleteBuffer(_sphereVBO);
                _gl.DeleteBuffer(_sphereIBO);
                _gl.DeleteBuffer(_sphereInstanceVBO);
                _gl.DeleteVertexArray(_sphereVAO);
                //_objectManager?.Dispose();
                GPUManager?.Dispose();
                _disposed = true;
            }
        }
    }
}