using Chorizite.OpenGLSDLBackend;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using WorldBuilder.Editors.Landscape;
using WorldBuilder.Lib;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Editors.Landscape.ViewModels {
    public partial class LandscapeEditorViewModel : ViewModelBase {
        [ObservableProperty] private ObservableCollection<ToolViewModelBase> _tools = new();

        [ObservableProperty]
        private SubToolViewModelBase? _selectedSubTool;

        [ObservableProperty]
        private ToolViewModelBase? _selectedTool;

        [ObservableProperty]
        private HistorySnapshotPanelViewModel? _historySnapshotPanel;

        [ObservableProperty]
        private LayersViewModel? _layersPanel;

        private Project? _project;
        private IDatReaderWriter? _dats;
        public TerrainSystem? TerrainSystem { get; private set; }
        public WorldBuilderSettings Settings { get; }

        private readonly ILogger<TerrainSystem> _logger;

        public LandscapeEditorViewModel(WorldBuilderSettings settings, ILogger<TerrainSystem> logger) {
            Settings = settings;
            _logger = logger;
        }

        internal void Init(Project project, OpenGLRenderer render, Avalonia.PixelSize canvasSize) {
            _dats = project.DocumentManager.Dats;
            _project = project;

            TerrainSystem = new TerrainSystem(render, project, _dats, Settings, _logger);

            Tools.Add(TerrainSystem.Services.GetRequiredService<TexturePaintingToolViewModel>());
            Tools.Add(TerrainSystem.Services.GetRequiredService<RoadDrawingToolViewModel>());

            if (Tools.Count > 0 && Tools[0].AllSubTools.Count > 0) {
                SelectSubTool(Tools[0].AllSubTools[0]);
            }

            var documentStorageService = project.DocumentManager.DocumentStorageService;
            HistorySnapshotPanel = new HistorySnapshotPanelViewModel(TerrainSystem, documentStorageService, TerrainSystem.History);
            LayersPanel = new LayersViewModel(TerrainSystem);

            UpdateTerrain(canvasSize);
        }

        internal void DoRender(Avalonia.PixelSize canvasSize) {
            if (TerrainSystem == null) return;

            UpdateTerrain(canvasSize);

            TerrainSystem.Scene.Render(
                TerrainSystem.Scene.CameraManager.Current,
                (float)canvasSize.Width / canvasSize.Height,
                TerrainSystem.EditingContext,
                canvasSize.Width,
                canvasSize.Height);
        }

        private void UpdateTerrain(Avalonia.PixelSize canvasSize) {
            if (TerrainSystem == null) return;

            TerrainSystem.Scene.CameraManager.Current.ScreenSize = new Vector2(canvasSize.Width, canvasSize.Height);
            var view = TerrainSystem.Scene.CameraManager.Current.GetViewMatrix();
            var projection = TerrainSystem.Scene.CameraManager.Current.GetProjectionMatrix();
            var viewProjection = view * projection;

            TerrainSystem.Update(TerrainSystem.Scene.CameraManager.Current.Position, viewProjection);

            TerrainSystem.EditingContext.ClearModifiedLandblocks();
        }

        [RelayCommand]
        private void SelectSubTool(SubToolViewModelBase subTool) {
            SelectedTool?.OnDeactivated();
            SelectedSubTool?.OnDeactivated();
            if (SelectedSubTool != null) SelectedSubTool.IsSelected = false;

            var parentTool = Tools.FirstOrDefault(t => t.AllSubTools.Contains(subTool));

            SelectedTool = parentTool;
            SelectedSubTool = subTool;
            parentTool?.OnActivated();
            parentTool?.ActivateSubTool(subTool);
            SelectedSubTool.IsSelected = true;
        }

        public void Cleanup() {
            TerrainSystem?.Dispose();
        }
    }

}