using Avalonia.Controls;
using System;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Views;

public partial class TextInputWindow : Window {
    public TextInputWindow() {
        InitializeComponent();
        DataContextChanged += TextInputWindow_DataContextChanged;
    }

    private void TextInputWindow_DataContextChanged(object? sender, EventArgs e) {
        if (DataContext is TextInputWindowViewModel vm) {
            vm.RequestClose += (s, ev) => Close();
            
            Opened += (s, ev) => {
                var textBox = this.FindControl<TextBox>("InputTextBox");
                textBox?.Focus();
            };
        }
    }
}
