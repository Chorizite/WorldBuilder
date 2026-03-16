using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using WorldBuilder.Shared.Services;
using ACE.Database.Models.World;
using Microsoft.Data.Sqlite;

namespace WorldBuilder.Shared.Tests.Services {
    public class KeywordRepositoryServiceTests : IDisposable {
        private readonly string _testDir;
        private readonly string _aceDbPath;
        private readonly Mock<ILogger<KeywordRepositoryService>> _loggerMock;
        private readonly Mock<IDatRepositoryService> _datRepoMock;
        private readonly Mock<IAceRepositoryService> _aceRepoMock;
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

            _aceRepoMock.Setup(r => r.GetAceDbPath(_aceId, It.IsAny<string>())).Returns(_aceDbPath);

            _service = new KeywordRepositoryService(_loggerMock.Object, _datRepoMock.Object, _aceRepoMock.Object);
            _service.SetRepositoryRoot(_testDir);
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
                    Type = 1
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
                    Type = 16, // Description
                    Value = "A very sharp sword"
                });

                await context.SaveChangesAsync();
            }
        }

        [Fact]
        public async Task GenerateAsync_ExtractsKeywordsFromAceDb() {
            // Arrange
            await CreateSeedAceDbAsync();

            // Act
            var result = await _service.GenerateAsync(_datId, _aceId, null, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            
            var keywords = await _service.GetKeywordsForSetupAsync(_datId, _aceId, 100, CancellationToken.None);
            Assert.True(keywords.HasValue);
            
            // Should contain class name and string values in correct columns
            Assert.Contains("TestWeenie", keywords.Value.Names, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Awesome Sword", keywords.Value.Names, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("very sharp sword", keywords.Value.Descriptions, StringComparison.OrdinalIgnoreCase);
        }
    }
}
