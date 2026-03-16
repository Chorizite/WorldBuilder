using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Views {
    public partial class AceDatabaseSelectionView : UserControl {
        public AceDatabaseSelectionView() {
            InitializeComponent();
        }

        private void InitializeComponent() {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
