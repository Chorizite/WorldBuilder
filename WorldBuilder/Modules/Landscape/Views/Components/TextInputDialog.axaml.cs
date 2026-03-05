using Avalonia.Controls;
using WorldBuilder.Modules.Landscape.ViewModels;

namespace WorldBuilder.Modules.Landscape.Views.Components;

public partial class TextInputDialog : Window {
    public TextInputDialog() {
        InitializeComponent();
        DataContextChanged += TextInputDialog_DataContextChanged;
    }

    private void TextInputDialog_DataContextChanged(object? sender, EventArgs e) {
        if (DataContext is TextInputDialogViewModel vm) {
            vm.RequestClose += (s, ev) => Close();

            Opened += (s, ev) => {
                var textBox = this.FindControl<TextBox>("InputTextBox");
                textBox?.Focus();
                textBox?.SelectAll();
            };
        }
    }
}
