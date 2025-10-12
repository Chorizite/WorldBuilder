using Chorizite.OpenGLSDLBackend;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
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

        private Project? _project;
        private IDatReaderWriter? _dats;
        public TerrainSystem? TerrainSystem { get; private set; }
        public WorldBuilderSettings Settings { get; }

        public LandscapeEditorViewModel(WorldBuilderSettings settings) {
            Settings = settings;
        }

        internal void Init(Project project, OpenGLRenderer render, Avalonia.PixelSize canvasSize) {
            _dats = project.DocumentManager.Dats;
            _project = project;

            TerrainSystem = new TerrainSystem(render, project, _dats, Settings);

            Tools.Add(TerrainSystem.Services.GetRequiredService<TexturePaintingToolViewModel>());
            Tools.Add(TerrainSystem.Services.GetRequiredService<RoadDrawingToolViewModel>());

            if (Tools.Count > 0 && Tools[0].AllSubTools.Count > 0) {
                SelectSubTool(Tools[0].AllSubTools[0]);
            }

            var documentStorageService = project.DocumentManager.DocumentStorageService;
            HistorySnapshotPanel = new HistorySnapshotPanelViewModel(TerrainSystem, documentStorageService, TerrainSystem.History);

            UpdateTerrain(canvasSize);
        }

        internal void DoRender(Avalonia.PixelSize canvasSize) {
            if (TerrainSystem == null) return;

            UpdateTerrain(canvasSize);

            TerrainSystem.Renderer.Render(
                TerrainSystem.CameraManager.Current,
                (float)canvasSize.Width / canvasSize.Height,
                TerrainSystem,
                TerrainSystem.EditingContext,
                canvasSize.Width,
                canvasSize.Height);
        }

        private void UpdateTerrain(Avalonia.PixelSize canvasSize) {
            if (TerrainSystem == null) return;

            TerrainSystem.CameraManager.Current.ScreenSize = new Vector2(canvasSize.Width, canvasSize.Height);
            var view = TerrainSystem.CameraManager.Current.GetViewMatrix();
            var projection = TerrainSystem.CameraManager.Current.GetProjectionMatrix();
            var viewProjection = view * projection;

            TerrainSystem.Update(TerrainSystem.CameraManager.Current.Position, viewProjection);

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