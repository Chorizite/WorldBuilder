using ACE.Database.Models.World;
using ACE.Entity.Enum.Properties;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Options;
using DatReaderWriter.Types;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Services;
using Xunit;

namespace WorldBuilder.Shared.Tests.Services {
    public class KeywordRepositoryServiceTests : IDisposable {
        private readonly string _testDir;
        private readonly string _aceDbPath;
        private readonly Mock<ILogger<KeywordRepositoryService>> _loggerMock;
        private readonly Mock<IDatRepositoryService> _datRepoMock;
        private readonly Mock<IAceRepositoryService> _aceRepoMock;
        private readonly Mock<IDatReaderWriter> _datReaderMock;
        private readonly Mock<IDatDatabase> _portalDbMock;
        private readonly KeywordRepositoryService _service;
        private readonly Guid _datId = Guid.NewGuid();
        private readonly Guid _aceId = Guid.NewGuid();

        public KeywordRepositoryServiceTests() {
            _testDir = Path.Combine(Path.GetTempPath(), "WorldBuilderTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_testDir);
            _aceDbPath = Path.Combine(_testDir, "ace.db");

            _loggerMock = new Mock<ILogger<KeywordRepositoryService>>();
            _datRepoMock = new Mock<IDatRepositoryService>();
            _aceRepoMock = new Mock<IAceRepositoryService>();
            _datReaderMock = new Mock<IDatReaderWriter>();
            _portalDbMock = new Mock<IDatDatabase>();

            _aceRepoMock.Setup(r => r.GetAceDbPath(_aceId, It.IsAny<string>())).Returns(_aceDbPath);
            _datRepoMock.Setup(r => r.GetDatSetPath(_datId, It.IsAny<string>())).Returns(_testDir);
            _datRepoMock.Setup(r => r.GetDatReaderWriter(It.IsAny<string>())).Returns(_datReaderMock.Object);

            _datReaderMock.Setup(r => r.Portal).Returns(_portalDbMock.Object);
            _datReaderMock.Setup(r => r.CellRegions).Returns(new ReadOnlyDictionary<uint, IDatDatabase>(new Dictionary<uint, IDatDatabase>()));

            _service = new KeywordRepositoryService(_loggerMock.Object, _datRepoMock.Object, _aceRepoMock.Object, new System.Net.Http.HttpClient());
            _service.SetRepositoryRoot(_testDir);
            _service.SetModelsRoot(Path.Combine(_testDir, "models"));
        }

        public void Dispose() {
            if (Directory.Exists(_testDir)) {
                Directory.Delete(_testDir, true);
            }
        }

        private async Task CreateSeedAceDbAsync() {
            var options = new DbContextOptionsBuilder<WorldDbContext>()
                .UseSqlite($"Data Source={_aceDbPath}")
                .Options;

            using (var context = new WorldDbContext(options)) {
                await context.Database.EnsureCreatedAsync();

                // Add a weenie with a setup ID and some string properties
                var weenie = new Weenie {
                    ClassId = 1,
                    ClassName = "TestWeenie",
                    Type = 1 // Generic
                };
                context.Weenie.Add(weenie);

                context.WeeniePropertiesDID.Add(new WeeniePropertiesDID {
                    ObjectId = 1,
                    Type = 1, // Setup
                    Value = 100
                });

                context.WeeniePropertiesString.Add(new WeeniePropertiesString {
                    ObjectId = 1,
                    Type = 1, // Name
                    Value = "Awesome Sword"
                });

                context.WeeniePropertiesString.Add(new WeeniePropertiesString {
                    ObjectId = 1,
                    Type = 16, // LongDesc
                    Value = "A very sharp sword"
                });

                // Add another weenie with GeneratorRadius that should be ignored
                var weenie2 = new Weenie {
                    ClassId = 2,
                    ClassName = "IgnoreMeWeenie",
                    Type = 1
                };
                context.Weenie.Add(weenie2);

                context.WeeniePropertiesDID.Add(new WeeniePropertiesDID {
                    ObjectId = 2,
                    Type = 1, // Setup
                    Value = 101
                });

                context.WeeniePropertiesString.Add(new WeeniePropertiesString {
                    ObjectId = 2,
                    Type = 1, // Name
                    Value = "Generator Thing"
                });

                context.WeeniePropertiesFloat.Add(new WeeniePropertiesFloat {
                    ObjectId = 2,
                    Type = (ushort)PropertyFloat.GeneratorRadius,
                    Value = 10.0
                });

                await context.SaveChangesAsync();
            }
        }

        [Fact]
        public async Task GenerateAsync_ExtractsKeywordsFromAceDb() {
            // Arrange
            await CreateSeedAceDbAsync();
            _portalDbMock.Setup(db => db.GetAllIdsOfType<Scene>()).Returns([]);

            // Act
            var result = await _service.GenerateAsync(_datId, _aceId, false, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
            
            var keywords = await _service.GetKeywordsForSetupAsync(_datId, _aceId, 100, CancellationToken.None);
            Assert.True(keywords.HasValue);
            
            // Should contain string values in correct columns
            Assert.DoesNotContain("TestWeenie", keywords.Value.Names, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Awesome Sword", keywords.Value.Names, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Generic", keywords.Value.Tags, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("very sharp sword", keywords.Value.Descriptions, StringComparison.OrdinalIgnoreCase);

            var keywords2 = await _service.GetKeywordsForSetupAsync(_datId, _aceId, 101, CancellationToken.None);
            Assert.False(keywords2.HasValue);
        }

        [Fact]
        public async Task GenerateAsync_ExtractsSceneryKeywordsFromPortalDat() {
            // Arrange
            await CreateSeedAceDbAsync();
            
            var sceneId = 0x12000001u;
            var scene = new Scene {
                Id = sceneId,
                Objects = new List<ObjectDesc> {
                    new ObjectDesc {
                        ObjectId = 0x02000064, // Matches setup ID 100 (0x64) from CreateSeedAceDbAsync
                        BaseLoc = new Frame()
                    }
                }
            };
            
            _portalDbMock.Setup(db => db.GetAllIdsOfType<Scene>()).Returns([sceneId]);
            Scene? outScene = scene;
            _portalDbMock.Setup(db => db.TryGet<Scene>(sceneId, out outScene)).Returns(true);

            // Act
            var result = await _service.GenerateAsync(_datId, _aceId, false, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
            
            var keywords = await _service.GetKeywordsForSetupAsync(_datId, _aceId, 0x02000064, CancellationToken.None);
            Assert.True(keywords.HasValue);
            
            Assert.Contains("scenery", keywords.Value.Tags, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GenerateAsync_ExtractsSceneryKeywordsFromPortalDat_NoWeenie() {
            // Arrange
            await CreateSeedAceDbAsync();
            
            var sceneId = 0x12000002u;
            var scene = new Scene {
                Id = sceneId,
                Objects = new List<ObjectDesc> {
                    new ObjectDesc {
                        ObjectId = 0x020000FF, // NOT in CreateSeedAceDbAsync
                        BaseLoc = new Frame()
                    }
                }
            };
            
            _portalDbMock.Setup(db => db.GetAllIdsOfType<Scene>()).Returns([sceneId]);
            Scene? outScene = scene;
            _portalDbMock.Setup(db => db.TryGet<Scene>(sceneId, out outScene)).Returns(true);

            // Act
            var result = await _service.GenerateAsync(_datId, _aceId, false, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
            
            var keywords = await _service.GetKeywordsForSetupAsync(_datId, _aceId, 0x020000FF, CancellationToken.None);
            Assert.True(keywords.HasValue);
            
            Assert.Contains("scenery", keywords.Value.Tags, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(string.Empty, keywords.Value.Names);
            Assert.Equal("Category: Scenery", keywords.Value.Descriptions);
        }

        [Fact]
        public async Task SearchSetupsAsync_FallsBackToTextSearchWhenEmbeddingsNotGenerated() {
            // Arrange
            await CreateSeedAceDbAsync();
            _portalDbMock.Setup(db => db.GetAllIdsOfType<Scene>()).Returns([]);
            await _service.GenerateAsync(_datId, _aceId, false, CancellationToken.None);

            // Act
            var results = await _service.SearchSetupsAsync(_datId, _aceId, "Awesome", SearchType.Hybrid, CancellationToken.None);

            // Assert
            Assert.Single(results);
            Assert.Equal(100u, results[0]);
        }
    }
}
