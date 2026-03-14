using Chorizite.OpenGLSDLBackend;
using Moq;
using WorldBuilder.Modules.Landscape;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;
using Microsoft.Extensions.Logging;
using HanumanInstitute.MvvmDialogs;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Modules.Landscape;
using WorldBuilder.Shared.Modules.Landscape.Services;
using System.Linq;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Services;
using Xunit;

using WorldBuilder.Shared.Tests.Helpers;

namespace WorldBuilder.Tests.Modules.Landscape {
    public class LandscapeViewModelTests : IDisposable {
        private readonly string _testSettingsDir;

        public LandscapeViewModelTests() {
            _testSettingsDir = TestSettingsHelper.SetupTestSettings();
        }

        public void Dispose() {
            TestSettingsHelper.CleanupTestSettings(_testSettingsDir);
        }

        [Fact]
        public void Constructor_SetsFirstToolAsActive() {
            var projectMock = new Mock<IProject>();
            var datsMock = new Mock<IDatReaderWriter>();
            var portalServiceMock = new Mock<IPortalService>();
            var docManagerMock = new Mock<IDocumentManager>();
            var loggerMock = new Mock<ILogger<LandscapeViewModel>>();
            var dialogServiceMock = new Mock<IDialogService>();
            var bookmarksManagerMock = new Mock<BookmarksManager>();
            var landscapeObjectServiceMock = new Mock<ILandscapeObjectService>();
            
            projectMock.Setup(p => p.IsReadOnly).Returns(false);
            
            var settings = new WorldBuilderSettings { Project = new ProjectSettings() };
            var vm = new LandscapeViewModel(projectMock.Object, datsMock.Object, portalServiceMock.Object, docManagerMock.Object, bookmarksManagerMock.Object, loggerMock.Object, dialogServiceMock.Object, settings, landscapeObjectServiceMock.Object);
            
            Assert.IsType<BrushTool>(vm.ActiveTool);
        }

        [Fact]
        public void ActiveTool_DefaultsToBrushTool_AndDisablesDebugShapes() {
            var projectMock = new Mock<IProject>();
            var datsMock = new Mock<IDatReaderWriter>();
            var portalServiceMock = new Mock<IPortalService>();
            var docManagerMock = new Mock<IDocumentManager>();
            var loggerMock = new Mock<ILogger<LandscapeViewModel>>();
            var dialogServiceMock = new Mock<IDialogService>();
            var bookmarksManagerMock = new Mock<BookmarksManager>();
            var landscapeObjectServiceMock = new Mock<ILandscapeObjectService>();
            
            projectMock.Setup(p => p.IsReadOnly).Returns(false);
            
            var settings = new WorldBuilderSettings { Project = new ProjectSettings() };
            var vm = new LandscapeViewModel(projectMock.Object, datsMock.Object, portalServiceMock.Object, docManagerMock.Object, bookmarksManagerMock.Object, loggerMock.Object, dialogServiceMock.Object, settings, landscapeObjectServiceMock.Object);
            
            Assert.IsType<BrushTool>(vm.ActiveTool);
            Assert.False(vm.IsDebugShapesEnabled);
        }

        [Fact]
        public void ActiveToolChanged_ToInspectorTool_EnablesDebugShapes() {
            var projectMock = new Mock<IProject>();
            var datsMock = new Mock<IDatReaderWriter>();
            var portalServiceMock = new Mock<IPortalService>();
            var docManagerMock = new Mock<IDocumentManager>();
            var loggerMock = new Mock<ILogger<LandscapeViewModel>>();
            var dialogServiceMock = new Mock<IDialogService>();
            var bookmarksManagerMock = new Mock<BookmarksManager>();
            var landscapeObjectServiceMock = new Mock<ILandscapeObjectService>();
            
            projectMock.Setup(p => p.IsReadOnly).Returns(false);
            
            var settings = new WorldBuilderSettings { Project = new ProjectSettings() };
            var vm = new LandscapeViewModel(projectMock.Object, datsMock.Object, portalServiceMock.Object, docManagerMock.Object, bookmarksManagerMock.Object, loggerMock.Object, dialogServiceMock.Object, settings, landscapeObjectServiceMock.Object);
            
            var inspectorTool = vm.Tools.OfType<InspectorTool>().First();
            vm.ActiveTool = inspectorTool;
            
            Assert.True(vm.IsDebugShapesEnabled);
        }

        [Fact]
        public void ActiveToolChanged_BackFromInspectorTool_DisablesDebugShapes() {
            var projectMock = new Mock<IProject>();
            var datsMock = new Mock<IDatReaderWriter>();
            var portalServiceMock = new Mock<IPortalService>();
            var docManagerMock = new Mock<IDocumentManager>();
            var loggerMock = new Mock<ILogger<LandscapeViewModel>>();
            var dialogServiceMock = new Mock<IDialogService>();
            var bookmarksManagerMock = new Mock<BookmarksManager>();
            var landscapeObjectServiceMock = new Mock<ILandscapeObjectService>();
            
            projectMock.Setup(p => p.IsReadOnly).Returns(false);
            
            var settings = new WorldBuilderSettings { Project = new ProjectSettings() };
            var vm = new LandscapeViewModel(projectMock.Object, datsMock.Object, portalServiceMock.Object, docManagerMock.Object, bookmarksManagerMock.Object, loggerMock.Object, dialogServiceMock.Object, settings, landscapeObjectServiceMock.Object);
            
            var brushTool = vm.Tools.OfType<BrushTool>().First();
            var inspectorTool = vm.Tools.OfType<InspectorTool>().First();
            
            vm.ActiveTool = inspectorTool;
            Assert.True(vm.IsDebugShapesEnabled);
            
            vm.ActiveTool = brushTool;
            Assert.False(vm.IsDebugShapesEnabled);
        }
    }
}
