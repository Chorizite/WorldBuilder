using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace WorldBuilder.Views {
    public partial class DatBrowserWindow : Window {
        public DatBrowserWindow() {
            InitializeComponent();
        }

        private void InitializeComponent() {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
