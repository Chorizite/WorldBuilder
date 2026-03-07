using Avalonia.Controls;
using WorldBuilder.Services;

namespace WorldBuilder.Views;

public partial class AppLogWindow : Window {
    public AppLogWindow() {
        InitializeComponent();
    }

    public AppLogWindow(AppLogService logService) : this() {
        DataContext = logService;
    }
}