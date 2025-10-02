
using Chorizite.OpenGLSDLBackend;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.ViewModels;
using WorldBuilder.ViewModels.Editors.LandscapeEditor;

namespace WorldBuilder.Test {
    public partial class LandscapeEditorViewModel : ViewModelBase {
        [ObservableProperty]
        private ObservableCollection<ToolViewModelBase> _tools = new();

        [ObservableProperty]
        private SubToolViewModelBase? _selectedSubTool;

        private Project _project;
        private IDatReaderWriter _dats;

        // Core systems
        public PerspectiveCamera PerspectiveCamera { get; private set; }
        public OrthographicTopDownCamera TopDownCamera { get; private set; }
        public CameraManager CameraManager { get; private set; }
        public TerrainDocument TerrainDoc { get; private set; }
        public TerrainSystem TerrainSystem { get; private set; }
        public TerrainEditingContext EditingContext { get; private set; }
        public TerrainRenderer Renderer { get; private set; }

        public LandscapeEditorViewModel(
            TexturePaintingToolViewModel texturePaintingTool,
            RoadDrawingToolViewModel roadDrawingTool) {

            Tools.Add(texturePaintingTool);
            Tools.Add(roadDrawingTool);

            // Select the first sub-tool of the first tool by default
            if (Tools.Count > 0 && Tools[0].AllSubTools.Count > 0) {
                SelectSubTool(Tools[0].AllSubTools[0]);
            }
        }

        internal void Init(Project project, OpenGLRenderer render, Avalonia.PixelSize canvasSize) {
            _project = project;
            _dats = project.DocumentManager.Dats;

            var sw = Stopwatch.StartNew();

            // Load terrain document
            TerrainDoc = project.DocumentManager.GetOrCreateDocumentAsync<TerrainDocument>("terrain").Result;
            Console.WriteLine($"Loaded terrain document in {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            // Initialize terrain system (replaces old TerrainProvider)
            TerrainSystem = new TerrainSystem(render, TerrainDoc, _dats, chunkSizeInLandblocks: 64);
            Console.WriteLine($"Initialized terrain system in {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            // Initialize editing context
            EditingContext = new TerrainEditingContext(TerrainDoc, TerrainSystem);
            Console.WriteLine($"Initialized editing context in {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            // Initialize cameras
            var mapCenter = new Vector3(192f * 254f / 2f, 192f * 254f / 2f, 1000);
            PerspectiveCamera = new PerspectiveCamera(mapCenter, Vector3.UnitZ);
            TopDownCamera = new OrthographicTopDownCamera(new Vector3(0, 0, 200f));
            CameraManager = new CameraManager(TopDownCamera);
            Console.WriteLine($"Initialized cameras in {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            // Initialize renderer
            Renderer = new TerrainRenderer(render);
            Console.WriteLine($"Initialized terrain renderer in {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            // Force initial terrain update
            UpdateTerrain(canvasSize);
            Console.WriteLine($"Initial terrain update in {sw.ElapsedMilliseconds}ms");
        }

        internal void DoRender(Avalonia.PixelSize canvasSize) {
            if (Renderer == null || TerrainSystem == null) return;

            // Update terrain chunks and GPU resources
            UpdateTerrain(canvasSize);

            // Render the terrain
            Renderer.Render(
                CameraManager.Current,
                (float)canvasSize.Width / canvasSize.Height,
                TerrainSystem,
                EditingContext,
                canvasSize.Width,
                canvasSize.Height);
        }

        private void UpdateTerrain(Avalonia.PixelSize canvasSize) {
            if (TerrainSystem == null) return;

            var view = CameraManager.Current.GetViewMatrix();
            var projection = CameraManager.Current.GetProjectionMatrix(
                (float)canvasSize.Width / canvasSize.Height,
                1.0f,
                80000f);
            var viewProjection = view * projection;

            // Update terrain system (handles chunk streaming and GPU updates)
            TerrainSystem.Update(CameraManager.Current.Position, viewProjection);

            // Clear modified landblock flags after GPU updates
            EditingContext.ClearModifiedLandblocks();
        }

        [RelayCommand]
        private void SelectSubTool(SubToolViewModelBase subTool) {
            // Find and activate the parent tool
            var parentTool = Tools.FirstOrDefault(t => t.AllSubTools.Contains(subTool));
            parentTool?.ActivateSubTool(subTool);

            SelectedSubTool = subTool;
        }

        public void Cleanup() {
            Renderer?.Dispose();
            TerrainSystem?.Dispose();
        }
    }
}