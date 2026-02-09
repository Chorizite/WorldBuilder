using System.Threading.Tasks;
using Xunit;
using Moq;
using WorldBuilder.Services;
using DatReaderWriter;
using Microsoft.Extensions.Logging;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using Avalonia.Headless;
using Avalonia;
using Avalonia.Platform;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Tests.Services
{
    public class TextureServiceTests
    {
        [Fact]
        public async Task GetTextureAsync_ReturnsNull_WhenTextureMissing()
        {
            // Arrange
            var mockDats = new Mock<IDatReaderWriter>();
            var mockLogger = new Mock<ILogger<TextureService>>();
            var service = new TextureService(mockDats.Object, mockLogger.Object);

            uint textureId = 999;
            SurfaceTexture st = null;
            mockDats.Setup(d => d.Portal.TryGet<SurfaceTexture>(textureId, out st))
                .Returns(false);

            // Act
            var result = await service.GetTextureAsync(textureId);

            // Assert
            Assert.Null(result);
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Could not find SurfaceTexture")),
                    It.IsAny<System.Exception>(),
                    It.IsAny<System.Func<It.IsAnyType, System.Exception, string>>()),
                Times.Once);
        }
    }
}
