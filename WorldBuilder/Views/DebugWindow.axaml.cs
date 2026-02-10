using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;
using Microsoft.Extensions.Logging;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Views
{
    public partial class DebugWindow : Window
    {
        public DebugWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
        
        /// <summary>
        /// Sets the landscape document to be rendered in this window
        /// </summary>
        public void SetLandscape(LandscapeDocument? landscapeDocument, IDatReaderWriter? dats)
        {
            var debugRenderView = this.FindControl<RenderView>("DebugRenderView");
            if (debugRenderView != null)
            {
                debugRenderView.LandscapeDocument = landscapeDocument;
                debugRenderView.Dats = dats;
            }
        }
    }
}
