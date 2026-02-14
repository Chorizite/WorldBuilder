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
    public void GetViewName_WindowViewModel_InViewModelsNamespace_ReturnsViewInViewsNamespace() {
        // Arrange
        var mockDats = new Moq.Mock<WorldBuilder.Shared.Services.IDatReaderWriter>();
        mockDats.Setup(d => d.PortalIteration).Returns(1);

        var mockWindowViewModel = new WorldBuilder.ViewModels.ExportDatsWindowViewModel(
            new WorldBuilder.Services.WorldBuilderSettings(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<WorldBuilder.Services.WorldBuilderSettings>.Instance
            ),
            mockDats.Object,
            null!
        );

        // Act
        var result = _locator.GetViewNamePublic(mockWindowViewModel);

        // Assert
        Assert.Equal("WorldBuilder.Views.ExportDatsWindow", result);
    }

    [Fact]
    public void GetViewName_DatBrowserViewModel_WithPreferWindows_ReturnsWindowName() {
        // Arrange
        var mockDats = new Moq.Mock<WorldBuilder.Shared.Services.IDatReaderWriter>();
        var mockPortal = new Moq.Mock<WorldBuilder.Shared.Services.IDatDatabase>();
        mockDats.Setup(d => d.Portal).Returns(mockPortal.Object);
        mockPortal.Setup(p => p.GetAllIdsOfType<DatReaderWriter.DBObjs.Setup>()).Returns(Enumerable.Empty<uint>());
        mockPortal.Setup(p => p.GetAllIdsOfType<DatReaderWriter.DBObjs.GfxObj>()).Returns(Enumerable.Empty<uint>());
        mockPortal.Setup(p => p.GetAllIdsOfType<DatReaderWriter.DBObjs.SurfaceTexture>()).Returns(Enumerable.Empty<uint>());
        
        var mockSetup = new Moq.Mock<WorldBuilder.Modules.DatBrowser.ViewModels.SetupBrowserViewModel>(mockDats.Object);
        var mockGfx = new Moq.Mock<WorldBuilder.Modules.DatBrowser.ViewModels.GfxObjBrowserViewModel>(mockDats.Object);
        var mockTex = new Moq.Mock<WorldBuilder.Modules.DatBrowser.ViewModels.TextureBrowserViewModel>(mockDats.Object, null!);
        var mockDialog = new Moq.Mock<HanumanInstitute.MvvmDialogs.IDialogService>();
        var mockServiceProvider = new Moq.Mock<IServiceProvider>();

        var viewModel = new WorldBuilder.Modules.DatBrowser.ViewModels.DatBrowserViewModel(
            mockSetup.Object,
            mockGfx.Object,
            mockTex.Object,
            mockDialog.Object,
            mockServiceProvider.Object,
            mockDats.Object
        );

        // Act
        var result = _windowLocator.GetViewNamePublic(viewModel);

        // Assert
        Assert.Equal("WorldBuilder.Modules.DatBrowser.Views.DatBrowserWindow", result);
    }
}