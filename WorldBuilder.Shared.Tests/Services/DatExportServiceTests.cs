using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape;
using WorldBuilder.Shared.Services;
using WorldBuilder.Shared.Tests.Mocks;
using Xunit;

namespace WorldBuilder.Shared.Tests.Services {
    public class DatExportServiceTests : IDisposable {
        private readonly Mock<ILandscapeModule> _landscapeModuleMock;
        private readonly Mock<IDocumentManager> _documentManagerMock;
        private readonly Mock<IDatReaderWriter> _datReaderWriterMock;
        private readonly DatExportService _service;
        private readonly string _testSourceDir;
        private readonly string _testExportDir;

        public DatExportServiceTests() {
            _landscapeModuleMock = new Mock<ILandscapeModule>();
            _documentManagerMock = new Mock<IDocumentManager>();
            _datReaderWriterMock = new Mock<IDatReaderWriter>();

            _testSourceDir = Path.Combine(Path.GetTempPath(), "WorldBuilderTests_Source_" + Guid.NewGuid());
            _testExportDir = Path.Combine(Path.GetTempPath(), "WorldBuilderTests_Export_" + Guid.NewGuid());

            Directory.CreateDirectory(_testSourceDir);
            File.WriteAllText(Path.Combine(_testSourceDir, "client_portal.dat"), "portal");
            File.WriteAllText(Path.Combine(_testSourceDir, "client_cell_1.dat"), "cell1");

            _datReaderWriterMock.Setup(d => d.SourceDirectory).Returns(_testSourceDir);
            _datReaderWriterMock.Setup(d => d.CellRegions).Returns(new System.Collections.ObjectModel.ReadOnlyDictionary<uint, IDatDatabase>(new Dictionary<uint, IDatDatabase> {
                { 1, new MockDatDatabase([]) }
            }));

            _service = new DatExportService(
                _datReaderWriterMock.Object,
                _documentManagerMock.Object,
                _landscapeModuleMock.Object,
                NullLogger<DatExportService>.Instance
            );
        }

        [Fact]
        public async Task ExportDatsAsync_CopiesFilesAndSavesDocuments() {
            // Arrange
            var regionId = 1u;
            var doc = new LandscapeDocument(regionId);
            var rental = new DocumentManager.DocumentRental<LandscapeDocument>(doc, () => { });

            var mockCellDb = new MockDatDatabase([]);
            _datReaderWriterMock.Setup(d => d.CellRegions).Returns(new System.Collections.ObjectModel.ReadOnlyDictionary<uint, IDatDatabase>(new Dictionary<uint, IDatDatabase> {
                { regionId, mockCellDb }
            }));
            _datReaderWriterMock.Setup(d => d.RegionFileMap).Returns(new System.Collections.ObjectModel.ReadOnlyDictionary<uint, uint>(new Dictionary<uint, uint> {
                { regionId, 0x13000001 }
            }));

            var portalMock = new Mock<IDatDatabase>();
            var region = new DatReaderWriter.DBObjs.Region { RegionNumber = 1, LandDefs = new DatReaderWriter.Types.LandDefs() };
            portalMock.Setup(p => p.TryGet<DatReaderWriter.DBObjs.Region>(0x13000001, out region)).Returns(true);
            _datReaderWriterMock.Setup(d => d.Portal).Returns(portalMock.Object);

            await doc.InitializeForUpdatingAsync(_datReaderWriterMock.Object, _documentManagerMock.Object, default);

            _documentManagerMock.Setup(m => m.RentDocumentAsync<LandscapeDocument>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WorldBuilder.Shared.Lib.Result<DocumentManager.DocumentRental<LandscapeDocument>>.Success(rental));

            // Act
            var result = await _service.ExportDatsAsync(_testExportDir, 1);

            // Assert
            Assert.True(result);
            Assert.True(File.Exists(Path.Combine(_testExportDir, "client_portal.dat")));
            Assert.True(File.Exists(Path.Combine(_testExportDir, "client_cell_1.dat")));

            _documentManagerMock.Verify(m => m.RentDocumentAsync<LandscapeDocument>(LandscapeDocument.GetIdFromRegion(regionId), It.IsAny<CancellationToken>()), Times.Once);
        }

        public void Dispose() {
            if (Directory.Exists(_testSourceDir)) Directory.Delete(_testSourceDir, true);
            if (Directory.Exists(_testExportDir)) Directory.Delete(_testExportDir, true);
        }
    }
}
