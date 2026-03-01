using Chorizite.OpenGLSDLBackend;
using Moq;
using WorldBuilder.Modules.Landscape;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;
using Microsoft.Extensions.Logging;
using HanumanInstitute.MvvmDialogs;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Modules.Landscape;
using System.Linq;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Shared.Modules.Landscape.Models;
using Xunit;

namespace WorldBuilder.Tests.Modules.Landscape {
    public class LandscapeViewModelTests {
        [Fact]
        public void Constructor_SetsFirstToolAsActive() {
            var projectMock = new Mock<IProject>();
            var datsMock = new Mock<IDatReaderWriter>();
            var portalServiceMock = new Mock<IPortalService>();
            var docManagerMock = new Mock<IDocumentManager>();
            var loggerMock = new Mock<ILogger<LandscapeViewModel>>();
            var dialogServiceMock = new Mock<IDialogService>();
            
            projectMock.Setup(p => p.IsReadOnly).Returns(false);
            
            var vm = new LandscapeViewModel(projectMock.Object, datsMock.Object, portalServiceMock.Object, docManagerMock.Object, loggerMock.Object, dialogServiceMock.Object);
            
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
            
            projectMock.Setup(p => p.IsReadOnly).Returns(false);
            
            var vm = new LandscapeViewModel(projectMock.Object, datsMock.Object, portalServiceMock.Object, docManagerMock.Object, loggerMock.Object, dialogServiceMock.Object);
            
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
            
            projectMock.Setup(p => p.IsReadOnly).Returns(false);
            
            var vm = new LandscapeViewModel(projectMock.Object, datsMock.Object, portalServiceMock.Object, docManagerMock.Object, loggerMock.Object, dialogServiceMock.Object);
            
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
            
            projectMock.Setup(p => p.IsReadOnly).Returns(false);
            
            var vm = new LandscapeViewModel(projectMock.Object, datsMock.Object, portalServiceMock.Object, docManagerMock.Object, loggerMock.Object, dialogServiceMock.Object);
            
            var brushTool = vm.Tools.OfType<BrushTool>().First();
            var inspectorTool = vm.Tools.OfType<InspectorTool>().First();
            
            vm.ActiveTool = inspectorTool;
            Assert.True(vm.IsDebugShapesEnabled);
            
            vm.ActiveTool = brushTool;
            Assert.False(vm.IsDebugShapesEnabled);
        }
    }
}
