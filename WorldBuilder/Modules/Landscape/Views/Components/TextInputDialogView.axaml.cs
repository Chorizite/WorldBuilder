using Avalonia.Controls;
using WorldBuilder.Modules.Landscape.ViewModels;

namespace WorldBuilder.Modules.Landscape.Views.Components;

public partial class TextInputDialogView : Window {
    public TextInputDialogView() {
        InitializeComponent();
        DataContextChanged += TextInputDialogView_DataContextChanged;
    }

    private void TextInputDialogView_DataContextChanged(object? sender, EventArgs e) {
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
