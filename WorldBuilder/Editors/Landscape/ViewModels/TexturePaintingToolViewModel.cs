using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.Enums;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Tools;

namespace WorldBuilder.Editors.Landscape.ViewModels {
    public partial class TexturePaintingToolViewModel : ToolViewModelBase {
        public override string Name => "Terrain";
        public override string IconGlyph => "🖌️";

        [ObservableProperty]
        private ObservableCollection<SubToolViewModelBase> _subTools = new();

        public override ObservableCollection<SubToolViewModelBase> AllSubTools => SubTools;

        public TexturePaintingToolViewModel(BrushSubToolViewModel brushSubTool, BucketFillSubToolViewModel bucketFillSubTool) {
            SubTools.Add(brushSubTool);
            SubTools.Add(bucketFillSubTool);
        }

        public override void OnActivated() {
            SelectedSubTool?.OnActivated();
        }

        public override void OnDeactivated() {
            SelectedSubTool?.OnDeactivated();
        }

        public override bool HandleMouseDown(MouseState mouseState) {
            return SelectedSubTool?.HandleMouseDown(mouseState) ?? false;
        }

        public override bool HandleMouseUp(MouseState mouseState) {
            return SelectedSubTool?.HandleMouseUp(mouseState) ?? false;
        }

        public override bool HandleMouseMove(MouseState mouseState) {
            return SelectedSubTool?.HandleMouseMove(mouseState) ?? false;
        }

        public override void Update(double deltaTime) {
            SelectedSubTool?.Update(deltaTime);
        }
    }
}