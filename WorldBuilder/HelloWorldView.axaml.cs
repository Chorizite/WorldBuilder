using Avalonia.Interactivity;
using Avalonia;

namespace WorldBuilder;

public partial class HelloWorldView : Avalonia.Controls.UserControl {
    public HelloWorldView() {
        InitializeComponent();
        Button1.Click += MyButton_Click;
        Button2.Click += MyButton2_Click;
        Console.WriteLine("HelloWorldView initialized");

    }

    private void MyButton_Click(object? sender, RoutedEventArgs e) {
        MyTextBox.Text = $"Hello Button 1 Clicked\n {DateTime.Now}";
        Console.WriteLine("Button 1 Clicked");
    }

    private void MyButton2_Click(object? sender, RoutedEventArgs e) {
        MyTextBox.Text = $"Hello Button 2 Clicked\n {DateTime.Now}";
        Console.WriteLine("Button 2 Clicked");
    }
}