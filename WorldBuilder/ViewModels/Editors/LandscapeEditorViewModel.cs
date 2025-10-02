using Chorizite.OpenGLSDLBackend;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Tools.Landscape;
using WorldBuilder.ViewModels.Editors.LandscapeEditor;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace WorldBuilder.ViewModels.Editors {
    public partial class LandscapeEditorViewModel : ViewModelBase {
        [ObservableProperty]
        private ObservableCollection<ToolViewModelBase> _tools = new();

        [ObservableProperty]
        private SubToolViewModelBase? _selectedSubTool;
        private Project _project;
        private IDatReaderWriter _dats;
        private int _loadedChunks;
        private int _visibleChunks;

        public PerspectiveCamera PerspectiveCamera;
        public OrthographicTopDownCamera TopDownCamera;
        public CameraManager CameraManager;
        public TerrainDocument? TerrainDoc;
        public TerrainProvider TerrainProvider;
        public TerrainEditingContext EditingContext;
        public TerrainRenderer Renderer;

        public LandscapeEditorViewModel(TexturePaintingToolViewModel texturePaintingTool, RoadDrawingToolViewModel roadDrawingTool) {
            Tools.Add(texturePaintingTool);
            Tools.Add(roadDrawingTool);

            // Select the first sub-tool of the first tool by default
            if (Tools.Count > 0 && Tools[0].AllSubTools.Count > 0) {
                SelectSubTool(Tools[0].AllSubTools[0]);
            }
        }

        internal void Init(Project project, OpenGLRenderer render, Avalonia.PixelSize CanvasSize) {
            _project = project;
            _dats = project.DocumentManager.Dats;

            var sw = Stopwatch.StartNew();
            TerrainDoc = project.DocumentManager.GetOrCreateDocumentAsync<TerrainDocument>("terrain").Result;
            Console.WriteLine($"Loaded terrain in {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            TerrainProvider = new TerrainProvider(render, TerrainDoc, _dats, 64);
            Console.WriteLine($"Initialized terrain generator in {sw.ElapsedMilliseconds}ms");

            // Initialize editing context
            EditingContext = new TerrainEditingContext(TerrainDoc, TerrainProvider);
            Console.WriteLine($"Initialized editing context in {sw.ElapsedMilliseconds}ms");

            PerspectiveCamera = new PerspectiveCamera(new Vector3(192f * 254f / 2f, 192f * 254f / 2f, 1000), Vector3.UnitZ);
            TopDownCamera = new OrthographicTopDownCamera(new Vector3(0, 0, 200f));

            Console.WriteLine($"Initialized cameras in {sw.ElapsedMilliseconds}ms");

            // Use camera manager
            CameraManager = new CameraManager(TopDownCamera);
            Console.WriteLine($"Initialized camera manager in {sw.ElapsedMilliseconds}ms");

            Renderer = new TerrainRenderer(render, TerrainProvider.LandSurf.TerrainAtlas, TerrainProvider.LandSurf.AlphaAtlas);
            Console.WriteLine($"Initialized terrain renderer in {sw.ElapsedMilliseconds}ms");

            // Force initial chunk loading
            UpdateChunkGeneration(CanvasSize);
            Console.WriteLine($"Initial chunk load in {sw.ElapsedMilliseconds}ms");
        }

        internal void DoRender(Avalonia.PixelSize CanvasSize) {
            if (Renderer == null || TerrainProvider == null) return;

            UpdateModifiedLandblocks();
            UpdateChunkGeneration(CanvasSize);

            var visibleChunks = TerrainProvider.GetVisibleChunks();
            Renderer.RenderChunks(CameraManager.Current, ((float)CanvasSize.Width / CanvasSize.Height), visibleChunks, EditingContext, CanvasSize.Width, CanvasSize.Height);
        }

        private void UpdateChunkGeneration(Avalonia.PixelSize CanvasSize) {
            if (TerrainProvider == null) return;

            var view = CameraManager.Current.GetViewMatrix();
            var projection = CameraManager.Current.GetProjectionMatrix(((float)CanvasSize.Width / CanvasSize.Height), 1.0f, 80000f);
            var viewProjection = view * projection;

            TerrainProvider.UpdateChunks(CameraManager.Current.Position, viewProjection);
        }

        private void UpdateModifiedLandblocks() {
            var modified = EditingContext.ModifiedLandblocks.ToArray();
            foreach (var lbId in modified) {
                TerrainProvider.UpdateLandblock((uint)(lbId >> 8) & 0xFF, (uint)lbId & 0xFF);
            }
            EditingContext.ClearModifiedLandblocks();
        }

        [RelayCommand]
        private void SelectSubTool(SubToolViewModelBase subTool) {
            // Update the parent tool's selected sub-tool
            var parentTool = Tools.FirstOrDefault(t => t.AllSubTools.Contains(subTool));
            parentTool?.ActivateSubTool(subTool);

            SelectedSubTool = subTool;
        }
    }
}