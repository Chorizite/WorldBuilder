using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using WorldBuilder.Views;
using Xunit;

namespace WorldBuilder.Tests.Views;

public class ErrorDetailsWindowTests {
    [AvaloniaFact]
    public void ErrorDetailsWindow_InitializesWithoutThrowing() {
        // Arrange & Act
        var window = new ErrorDetailsWindow();

        // Assert
        Assert.NotNull(window);
    }

    [AvaloniaFact]
    public void ErrorDetailsWindow_InitializesWithErrorMessage() {
        // Arrange
        var errorMessage = "Test error message";

        // Act
        var window = new ErrorDetailsWindow(errorMessage);

        // Assert
        Assert.NotNull(window);
    }

    [AvaloniaFact]
    public void ErrorDetailsWindow_HasCorrectTitle() {
        // Arrange
        var window = new ErrorDetailsWindow();

        // Act & Assert
        Assert.Equal("Error Details", window.Title);
    }

    [AvaloniaFact]
    public void ErrorDetailsWindow_HasExpectedSize() {
        // Arrange
        var window = new ErrorDetailsWindow();

        // Act & Assert
        Assert.Equal(500, window.Width);
        Assert.Equal(300, window.Height);
    }
}