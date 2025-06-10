using Avalonia.Interactivity;
using Avalonia;
using System;
using WorldBuilder.Fun;

namespace WorldBuilder.Views;

public partial class HelloWorldView : Avalonia.Controls.UserControl {
    public HelloWorldView() {
        InitializeComponent();
        Button2.Click += MyButton2_Click;
        Console.WriteLine("HelloWorldView initialized");

    }
    private void MyButton2_Click(object? sender, RoutedEventArgs e) {
        Console.WriteLine("Button 2 Clicked");
        Tayne.SHOW = !Tayne.SHOW;
    }
}