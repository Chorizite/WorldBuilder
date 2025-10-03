
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
using WorldBuilder.Editors.Landscape.ViewModels;
using WorldBuilder.Editors.Landscape;
using Microsoft.Extensions.DependencyInjection;

namespace WorldBuilder.Editors.Landscape.ViewModels {
    public partial class LandscapeEditorViewModel : ViewModelBase {
        [ObservableProperty]
        private ObservableCollection<ToolViewModelBase> _tools = new();

        [ObservableProperty]
        private SubToolViewModelBase? _selectedSubTool;

        [ObservableProperty]
        private ToolViewModelBase? _selectedTool;

        private Project _project;
        private IDatReaderWriter _dats;
        public TerrainSystem TerrainSystem { get; private set; }

        public LandscapeEditorViewModel() {
        }

        internal void Init(Project project, OpenGLRenderer render, Avalonia.PixelSize canvasSize) {
            _dats = project.DocumentManager.Dats;
            _project = project;

            TerrainSystem = new TerrainSystem(render, project, _dats);

            Tools.Add(TerrainSystem.Services.GetRequiredService<TexturePaintingToolViewModel>());
            Tools.Add(TerrainSystem.Services.GetRequiredService<RoadDrawingToolViewModel>());

            // Select the first sub-tool of the first tool by default
            if (Tools.Count > 0 && Tools[0].AllSubTools.Count > 0) {
                Console.WriteLine($"Selecting first sub-tool: {Tools[0].AllSubTools[0].Name}");
                SelectSubTool(Tools[0].AllSubTools[0]);
            }

            UpdateTerrain(canvasSize);
        }

        internal void DoRender(Avalonia.PixelSize canvasSize) {
            if (TerrainSystem == null) return;

            // Update terrain chunks and GPU resources
            UpdateTerrain(canvasSize);

            // Render the terrain
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

            var view = TerrainSystem.CameraManager.Current.GetViewMatrix();
            var projection = TerrainSystem.CameraManager.Current.GetProjectionMatrix(
                (float)canvasSize.Width / canvasSize.Height,
                1.0f,
                80000f);
            var viewProjection = view * projection;

            // Update terrain system (handles chunk streaming and GPU updates)
            TerrainSystem.Update(TerrainSystem.CameraManager.Current.Position, viewProjection);

            // Clear modified landblock flags after GPU updates
            TerrainSystem.EditingContext.ClearModifiedLandblocks();
        }

        [RelayCommand]
        private void SelectSubTool(SubToolViewModelBase subTool) {
            // deactivate the currently selected sub-tool
            SelectedTool?.OnDeactivated();
            SelectedSubTool?.OnDeactivated();
            if (SelectedSubTool != null) SelectedSubTool.IsSelected = false;

            // Find and activate the parent tool
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