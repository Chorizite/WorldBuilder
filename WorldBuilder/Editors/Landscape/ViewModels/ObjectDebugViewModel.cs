using Avalonia;
using Avalonia.Controls;
using Chorizite.OpenGLSDLBackend;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DatReaderWriter.DBObjs;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using WorldBuilder.Editors.Landscape;
using WorldBuilder.Lib;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Shared.Lib;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Editors.Landscape.ViewModels {
    public partial class ObjectDebugViewModel : ViewModelBase {
        private OpenGLRenderer _renderer;
        private IDatReaderWriter _dats;
        private StaticObjectManager _objectManager;
        private GL _gl;

        // --------------------------------------------------------------------
        // UI bound properties
        // --------------------------------------------------------------------
        [ObservableProperty] private string _objectIdText = "";
        [ObservableProperty] private string _status = "";
        [ObservableProperty] private uint? _selectedSetupId;
        [ObservableProperty] private uint? _selectedGfxObjId;

        partial void OnSelectedSetupIdChanged(uint? value) {
            if (value.HasValue) {
                SelectedGfxObjId = null; // Deselect other
                LoadFromId(value.Value);
            }
        }

        partial void OnSelectedGfxObjIdChanged(uint? value) {
            if (value.HasValue) {
                SelectedSetupId = null; // Deselect other
                LoadFromId(value.Value);
            }
        }
        // --------------------------------------------------------------------
        // Rendering state
        // --------------------------------------------------------------------
        private uint _currentId;
        private bool _isSetup;
        private StaticObjectRenderData? _renderData;
        private Matrix4x4 _modelMatrix = Matrix4x4.Identity;
        private PerspectiveCamera _camera;
        private IEnumerable<uint> _setupIds;
        private IEnumerable<uint> _gfxObjIds;

        [ObservableProperty] private IEnumerable<uint> _filteredSetupIds;
        [ObservableProperty] private IEnumerable<uint> _filteredGfxObjIds;
        [ObservableProperty] private string _searchText = "";

        // Camera controls
        private float _rotationAngleY = 0f;
        private float _rotationAngleX = 0f;
        private float _zoomDistanceMultiplier = 1f; // Starts at default, increase to zoom out/up

        public ObjectDebugViewModel() {
        }

        internal void Init(OpenGLRenderer renderer, IDatReaderWriter dats, StaticObjectManager staticObjectManager) {
            _renderer = renderer;
            _dats = dats;
            _objectManager = staticObjectManager;
            _gl = renderer.GraphicsDevice.GL;

            // Simple ortho camera that will be re-sized in Render()
            _camera = new PerspectiveCamera(new Vector3(0, 0, 10), new WorldBuilderSettings());

            _setupIds = _dats.Dats.Portal.GetAllIdsOfType<Setup>().OrderBy(id => id);
            _gfxObjIds = _dats.Dats.Portal.GetAllIdsOfType<GfxObj>().OrderBy(id => id);

            FilteredSetupIds = _setupIds;
            FilteredGfxObjIds = _gfxObjIds;
        }

        partial void OnSearchTextChanged(string value) {
            var query = value.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(query)) {
                FilteredSetupIds = _setupIds;
                FilteredGfxObjIds = _gfxObjIds;
            }
            else {
                uint parsed;
                var isHex = query.StartsWith("0x");
                var parseStr = isHex ? query.Substring(2) : query;
                var isNumeric = uint.TryParse(parseStr, isHex ? System.Globalization.NumberStyles.HexNumber : System.Globalization.NumberStyles.Integer, null, out parsed);

                FilteredSetupIds = _setupIds.Where(id => {
                    if (isNumeric) return id == parsed;
                    var hex = $"0x{id:X8}";
                    return id.ToString().Contains(query) || hex.Contains(query) || hex.ToLower().Contains(query);
                });

                FilteredGfxObjIds = _gfxObjIds.Where(id => {
                    if (isNumeric) return id == parsed;
                    var hex = $"0x{id:X8}";
                    return id.ToString().Contains(query) || hex.Contains(query) || hex.ToLower().Contains(query);
                });
            }
        }

        // --------------------------------------------------------------------
        // Load command
        // --------------------------------------------------------------------
        [RelayCommand]
        private void Load() {
            // run on avalonia ui thread
            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                Status = "";
                if (!TryParseId(ObjectIdText, out var id, out var isSetup)) {
                    Status = "Invalid ID (use hex with 0x or decimal)";
                    return;
                }

                _currentId = id;
                _isSetup = isSetup;

                // Grab (or create) render data – exactly what GameScene does
                _renderData = _objectManager.GetRenderData(id, isSetup);
                if (_renderData == null) {
                    Status = "Object not found in DATs";
                    return;
                }

                // Compute a rough AABB so we can auto-frame the camera
                var (min, max) = EstimateObjectBounds(_renderData);
                var size = max - min;
                var center = (min + max) * 0.5f;

                // Simple scale/translate so the object sits at origin and fits ~80% of view
                var maxDim = MathF.Max(MathF.Max(size.X, size.Y), size.Z);
                var scale = 1f; // Adjust if needed
                _modelMatrix = Matrix4x4.CreateScale(scale) *
                               Matrix4x4.CreateTranslation(-center);

                // Adjust base camera distance (zoomed out more initially)
                var baseCamDist = maxDim * 1.5f;
                _zoomDistanceMultiplier = 1f;
                _rotationAngleY = -45f;
                _rotationAngleX = 0f;
                UpdateCameraPosition(baseCamDist, center);

                Console.WriteLine($"Loaded 0x{id:X8} ({(isSetup ? "Setup" : "GfxObj")}): {size}");
                Console.WriteLine($"AABB: {min} - {max}");
                Console.WriteLine($"Center: {center}");
                Console.WriteLine($"Scale: {scale}");
                Console.WriteLine($"BaseCamDist: {baseCamDist}");

                Status = $"Loaded 0x{id:X8} ({(isSetup ? "Setup" : "GfxObj")})";
            });
        }

        private void UpdateCameraPosition(float baseDist, Vector3 target) {
            var dist = baseDist * _zoomDistanceMultiplier;
            var offset = new Vector3(
                MathF.Sin(_rotationAngleY) * MathF.Cos(_rotationAngleX) * dist,
                MathF.Sin(_rotationAngleX) * dist,
                MathF.Cos(_rotationAngleY) * MathF.Cos(_rotationAngleX) * dist
            );
            _camera.SetPosition(target + offset);
            _camera.LookAt(target);
        }

        [RelayCommand]
        private void LoadFromId(uint id) {
            ObjectIdText = "0x" + id.ToString("X8");
            Load();
        }

        // --------------------------------------------------------------------
        // Helper: parse hex / decimal + guess Setup vs GfxObj
        // --------------------------------------------------------------------
        private bool TryParseId(string input, out uint id, out bool isSetup) {
            id = 0;
            isSetup = (id & 0x02000000) != 0;

            if (string.IsNullOrWhiteSpace(input)) return false;
            var trimmed = input.Trim();

            var styles = System.Globalization.NumberStyles.Integer;
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
                styles = System.Globalization.NumberStyles.HexNumber;
                trimmed = trimmed[2..];
            }

            if (!uint.TryParse(trimmed, styles, null, out id))
                return false;

            isSetup = (id & 0x02000000) != 0;
            return true;
        }

        // --------------------------------------------------------------------
        // Very small AABB estimator – walks vertices of a GfxObj render data.
        // For Setups we recursively inspect each part (simplified here).
        // --------------------------------------------------------------------
        private (Vector3 Min, Vector3 Max) EstimateObjectBounds(StaticObjectRenderData data) {
            return (new Vector3(-1,-1, 0), new Vector3(1, 1, 1));
            /*
            if (data.IsSetup) {
                // For a Setup we need to pull each part’s bounds and transform them.
                var min = new Vector3(float.MaxValue);
                var max = new Vector3(float.MinValue);
                foreach (var (gfxId, transform) in data.SetupParts) {
                    var childData = _objectManager.GetRenderData(gfxId, false);
                    if (childData != null) {
                        // Approximate transformed AABB
                        var childMin = Vector3.Transform(childData.BoundsMin, transform);
                        var childMax = Vector3.Transform(childData.BoundsMax, transform);
                        min = Vector3.Min(min, childMin);
                        max = Vector3.Max(max, childMax);
                    }
                }
                return (min, max);
            }
            else {
                return (data.BoundsMin, data.BoundsMax);
            }
            */
        }

        // --------------------------------------------------------------------
        // Camera control methods (call these from UI gestures, e.g., mouse drag/scroll)
        // --------------------------------------------------------------------
        public void RotateAround(float deltaY, float deltaX) {
            _rotationAngleY += deltaY * 0.01f; // Horizontal rotation
            _rotationAngleX += deltaX * 0.01f; // Vertical rotation
            _rotationAngleX = Math.Clamp(_rotationAngleX, -MathF.PI / 2.1f, MathF.PI / 2.1f); // Limit up/down to avoid flip

            // Recompute position if object loaded
            if (_renderData != null) {
                var (min, max) = EstimateObjectBounds(_renderData);
                var center = (min + max) * 0.5f;
                var size = max - min;
                var maxDim = MathF.Max(MathF.Max(size.X, size.Y), size.Z);
                var baseCamDist = maxDim * 1.5f + 5f;
                UpdateCameraPosition(baseCamDist, center);
            }
        }

        public void Zoom(float delta) {
            _zoomDistanceMultiplier = Math.Clamp(_zoomDistanceMultiplier - delta * 0.1f, 0.5f, 10f); // Zoom out/up by increasing distance

            // Recompute position if object loaded
            if (_renderData != null) {
                var (min, max) = EstimateObjectBounds(_renderData);
                var center = (min + max) * 0.5f;
                var size = max - min;
                var maxDim = MathF.Max(MathF.Max(size.X, size.Y), size.Z);
                var baseCamDist = maxDim * 1.5f + 5f;
                UpdateCameraPosition(baseCamDist, center);
            }
        }

        public unsafe void Render(PixelSize canvasSize) {
            if (_renderData == null) return;

            var gl = _gl;
            gl.FrontFace(FrontFaceDirection.CW);
            gl.Enable(EnableCap.DepthTest);
            gl.ClearColor(0.1f, 0.1f, 0.1f, 1f);
            gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // Update camera aspect
            _camera.ScreenSize = new Vector2(canvasSize.Width, canvasSize.Height);
            var view = _camera.GetViewMatrix();
            var proj = _camera.GetProjectionMatrix();
            var vp = view * proj;

            var shader = _objectManager._objectShader;
            shader.Bind();
            shader.SetUniform("uViewProjection", vp);
            shader.SetUniform("uCameraPosition", _camera.Position);
            shader.SetUniform("uLightDirection", Vector3.Normalize(new Vector3(-0.5f, -0.5f, -0.3f)));
            shader.SetUniform("uAmbientIntensity", 0.4f);
            shader.SetUniform("uSpecularPower", 32f);

            if (_renderData.IsSetup) {
                RenderSetup(_renderData, _modelMatrix);
            }
            else {
                RenderGfxObj(_renderData, _modelMatrix);
            }

            // Cleanup shader
            shader.Unbind();
        }

        private unsafe void RenderGfxObj(StaticObjectRenderData data, Matrix4x4 model) {
            if (data.Batches.Count == 0 || data.VAO == 0) {
                Console.WriteLine("No batches or VAO for GfxObj");
                return;
            }

            var gl = _gl;
            var instanceVbo = CreateInstanceVbo(new[] { model });

            gl.BindVertexArray(data.VAO);

            // CRITICAL: Bind instance VBO before attrib pointers domestically
            gl.BindBuffer(GLEnum.ArrayBuffer, instanceVbo);
            for (int i = 0; i < 4; i++) {
                var attrLoc = (uint)(3 + i);
                gl.EnableVertexAttribArray(attrLoc);
                gl.VertexAttribPointer(attrLoc, 4, GLEnum.Float, false, 16 * sizeof(float), (void*)(i * 4 * sizeof(float)));
                gl.VertexAttribDivisor(attrLoc, 1);
            }
            gl.BindBuffer(GLEnum.ArrayBuffer, 0); // Unbind after setup

            foreach (var batch in data.Batches) {
                if (batch.TextureArray == null) {
                    Console.WriteLine($"Warning: TextureArray null for batch surface 0x{batch.SurfaceId:X8}");
                    continue;
                }

                if (!data.LocalAtlases[(batch.TextureSize.Width, batch.TextureSize.Height, batch.TextureFormat)].HasTexture(batch.Key)) {
                    Console.WriteLine($"Warning: Mismatch for surface 0x{batch.SurfaceId:X8}");
                }

                batch.TextureArray.Bind(0);
                var shader = _objectManager._objectShader;
                shader.SetUniform("uTextureArray", 0);
                shader.SetUniform("uTextureIndex", batch.TextureIndex); // Ensure shader casts to int if needed

                gl.BindBuffer(GLEnum.ElementArrayBuffer, batch.IBO);
                gl.DrawElementsInstanced(GLEnum.Triangles, (uint)batch.IndexCount, GLEnum.UnsignedShort, null, 1);
            }

            gl.BindVertexArray(0);
            gl.DeleteBuffer(instanceVbo);
        }

        private unsafe void RenderSetup(StaticObjectRenderData setupData, Matrix4x4 parentModel) {
            var gl = _gl;
            for (int i = 0; i < setupData.SetupParts.Count; i++) {
                var (gfxId, localTransform) = setupData.SetupParts[i];
                var worldModel = localTransform * parentModel;

                // Fetch child render data (cached in manager)
                var childData = _objectManager.GetRenderData(gfxId, false);
                if (childData != null) {
                    RenderGfxObj(childData, worldModel);
                }
            }
        }

        // --------------------------------------------------------------------
        // Helper: upload a single matrix as an instance buffer
        // --------------------------------------------------------------------
        private unsafe uint CreateInstanceVbo(Matrix4x4[] matrices) {
            uint vbo;
            _gl.GenBuffers(1, out vbo);
            _gl.BindBuffer(GLEnum.ArrayBuffer, vbo);

            var data = new float[matrices.Length * 16];
            for (int i = 0; i < matrices.Length; i++) {
                var m = matrices[i];
                data[i * 16 + 0] = m.M11; data[i * 16 + 1] = m.M12; data[i * 16 + 2] = m.M13; data[i * 16 + 3] = m.M14;
                data[i * 16 + 4] = m.M21; data[i * 16 + 5] = m.M22; data[i * 16 + 6] = m.M23; data[i * 16 + 7] = m.M24;
                data[i * 16 + 8] = m.M31; data[i * 16 + 9] = m.M32; data[i * 16 + 10] = m.M33; data[i * 16 + 11] = m.M34;
                data[i * 16 + 12] = m.M41; data[i * 16 + 13] = m.M42; data[i * 16 + 14] = m.M43; data[i * 16 + 15] = m.M44;
            }

            fixed (float* ptr = data)
                _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(data.Length * sizeof(float)), ptr, GLEnum.DynamicDraw);

            return vbo;
        }

        public void Dispose() {
            if (_renderData != null) {
                _objectManager.ReleaseRenderData(_currentId, _isSetup);
                _renderData = null;
            }
        }
    }
}