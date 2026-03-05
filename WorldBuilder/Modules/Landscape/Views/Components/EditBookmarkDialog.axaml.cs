using Avalonia.Controls;
using WorldBuilder.Modules.Landscape.ViewModels;

namespace WorldBuilder.Modules.Landscape.Views.Components;

public partial class EditBookmarkDialog : Window {
    public EditBookmarkDialog() {
        InitializeComponent();
        DataContextChanged += EditBookmarkDialog_DataContextChanged;
    }

    private void EditBookmarkDialog_DataContextChanged(object? sender, EventArgs e) {
        if (DataContext is EditBookmarkDialogViewModel vm) {
            vm.RequestClose += (s, ev) => Close();

            Opened += (s, ev) => {
                var textBox = this.FindControl<TextBox>("InputTextBox");
                textBox?.Focus();
                textBox?.SelectAll();
            };
        }
    }
}
