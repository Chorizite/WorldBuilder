using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Views {
    public partial class ExportDatsWindow : Window {
        public ExportDatsWindow() {
            InitializeComponent();
        }

        private void InitializeComponent() {
            AvaloniaXamlLoader.Load(this);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) {
            Close();
        }

        private async void Export_Click(object sender, RoutedEventArgs e) {
            if (DataContext is ExportDatsWindowViewModel vm) {
                await vm.ExportCommand.ExecuteAsync(null);
            }
        }
    }
}