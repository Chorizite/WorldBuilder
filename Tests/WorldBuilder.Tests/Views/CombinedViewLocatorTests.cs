using HanumanInstitute.MvvmDialogs.Avalonia;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using WorldBuilder.Services;
using Xunit;

namespace WorldBuilder.Tests.Views;

// Mock classes for testing different naming patterns - must be outside the test class to have correct full names
internal class MockWindowViewModel { }
internal class MockStandardViewModel { }
internal class MockModel { }
internal class MockViewModelViewModel { }
internal class MockWindowViewModelViewModel { }
internal class MockWindowView { }

// Mock classes for testing Components namespace
internal class MockComponentsViewModel { }
internal class MockComponentsView { }

// Test wrapper to expose the protected GetViewName method
public class TestableCombinedViewLocator : CombinedViewLocator {
    public TestableCombinedViewLocator(bool preferWindows = false) : base(preferWindows) { }
    public string GetViewNamePublic(object viewModel) => base.GetViewName(viewModel);
}

public class CombinedViewLocatorTests {
    private readonly TestableCombinedViewLocator _locator = new TestableCombinedViewLocator();
    private readonly TestableCombinedViewLocator _windowLocator = new TestableCombinedViewLocator(true);


    [Fact]
    public void GetViewName_StandardViewModel_ReturnsViewName() {
        // Arrange
        var mockStandardViewModel = new MockStandardViewModel();

        // Act
        var result = _locator.GetViewNamePublic(mockStandardViewModel);

        // Assert
        Assert.Equal("WorldBuilder.Tests.Views.MockStandardView", result);
    }

    [Fact]
    public void GetViewName_ViewModelEndingWithModel_ReturnsCorrectViewName() {
        // Arrange
        var mockViewModelEndingWithModel = new MockModel(); // Ends with "Model" not "ViewModel"

        // Act
        var result = _locator.GetViewNamePublic(mockViewModelEndingWithModel);

        // Assert
        Assert.Equal("WorldBuilder.Tests.Views.MockModel", result); // Should remain unchanged
    }

    [Fact]
    public void GetViewName_ViewModelWithMultipleViewModelOccurrences_ReplacesAllOccurrences() {
        // Arrange
        var mockMultiViewModel = new MockViewModelViewModel(); // Has "ViewModel" twice

        // Act
        var result = _locator.GetViewNamePublic(mockMultiViewModel);

        // Assert
        Assert.Equal("WorldBuilder.Tests.Views.MockViewView", result); // Both "ViewModel" occurrences replaced with "View"
    }

    [Fact]
    public void GetViewName_WindowViewModelWithMultipleOccurrences_ReplacesAllOccurrences() {
        // Arrange
        var mockWindowViewModelWithMultiple = new MockWindowViewModelViewModel(); // Has "WindowViewModel" pattern multiple times

        // Act
        var result = _locator.GetViewNamePublic(mockWindowViewModelWithMultiple);

        // Assert
        Assert.Equal("WorldBuilder.Tests.Views.MockWindowViewView", result); // All "ViewModel" occurrences replaced with "View"
    }

    [Fact]
    public void GetViewName_ViewModelWithSimilarButDifferentEnding_DoesNotChange() {
        // Arrange
        var mockSimilarViewModel = new MockWindowView(); // Ends with "View" not "ViewModel"

        // Act
        var result = _locator.GetViewNamePublic(mockSimilarViewModel);

        // Assert
        Assert.Equal("WorldBuilder.Tests.Views.MockWindowView", result); // Should remain unchanged
    }

    [Fact]
    public void GetViewName_ViewModelInStandardNamespace_StandardViewExists_ReturnsStandardViewName() {
        // Arrange
        var mockViewModel = new MockStandardViewModel();

        // Act
        var result = _locator.GetViewNamePublic(mockViewModel);

        // Assert - Should return standard Views namespace since MockStandardView exists
        Assert.Equal("WorldBuilder.Tests.Views.MockStandardView", result);
    }

    [Fact]
    public void GetViewName_ViewModelInStandardNamespace_StandardViewNotExists_ReturnsComponentsViewName() {
        // Arrange
        var mockViewModel = new MockComponentsViewModel();

        // Act
        var result = _locator.GetViewNamePublic(mockViewModel);

        // Assert - Should return standard view name as fallback since neither standard nor Components view exists
        Assert.Equal("WorldBuilder.Tests.Views.MockComponentsView", result);
    }
}