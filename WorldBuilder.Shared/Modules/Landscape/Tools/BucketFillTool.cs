using DatReaderWriter.Enums;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    /// <summary>
    /// A tool for filling connected areas of the same texture with a new texture.
    /// </summary>
    public class BucketFillTool : TexturePaintingToolBase {
        /// <inheritdoc/>
        public override string Name => "Paint Bucket";
        /// <inheritdoc/>
        public override string IconGlyph => "FormatColorFill";

        private bool _isContiguous = true;
        /// <summary>Gets or sets whether to fill only connected areas (flood fill) or globally replace.</summary>
        public bool IsContiguous {
            get => _isContiguous;
            set => SetProperty(ref _isContiguous, value);
        }

        private bool _onlyFillSameScenery = false;
        /// <summary>Gets or sets whether to only fill if the source scenery matches the target scenery.</summary>
        public bool OnlyFillSameScenery {
            get => _onlyFillSameScenery;
            set => SetProperty(ref _onlyFillSameScenery, value);
        }

        public override bool OnPointerPressed(ViewportInputEvent e) {
            if (Context == null || !e.IsLeftDown) return false;

            if (IsEyeDropperActive) {
                UpdateEyeDropper(e);
                IsEyeDropperActive = false;
                return true;
            }

            var hit = Raycast(e.Position.X, e.Position.Y);
            if (hit.Hit) {
                byte? sceneryIndex = (SelectedScenery == null || SelectedScenery.Index == 255) ? null : SelectedScenery.Index;
                var command = new BucketFillCommand(Context, hit.HitPosition, (int)Texture, sceneryIndex, IsContiguous, OnlyFillSameScenery);
                Context.CommandHistory.Execute(command);
                return true;
            }

            return false;
        }

        public override bool OnPointerMoved(ViewportInputEvent e) {
            if (Context == null) return false;

            if (IsEyeDropperActive) {
                UpdateEyeDropper(e);
                return true;
            }

            return false;
        }
    }
}
