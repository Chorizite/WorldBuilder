using MemoryPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;

namespace WorldBuilder.Shared.Tests.Events {
    public class BaseDocumentEventTests {
        [Fact]
        public void MemoryPack_SerializesAndDeserializes_UnionTypes() {
            // Arrange
            var original = new TerrainUpdateCommand {
                UserId = Guid.NewGuid().ToString(),
                Changes = new() {
                    { 0x1234, new TerrainEntry(100, 1, 2, 3, 0) }
                }
            };

            // Act
            var data = original.Serialize();
            var deserialized = BaseCommand.Deserialize<TerrainUpdateCommand>(data);

            // Assert
            Assert.IsType<TerrainUpdateCommand>(deserialized);
            var tue = (TerrainUpdateCommand)deserialized;

            Assert.NotNull(tue.Changes[0x1234]);

            Assert.Equal(original.Changes[0x1234]!.Value.Height, tue.Changes[0x1234]!.Value.Height);
            Assert.Equal(original.Changes[0x1234]!.Value.Road, tue.Changes[0x1234]!.Value.Road);

            Assert.True(original.Changes[0x1234]!.Value.Type.HasValue);
            Assert.True(tue.Changes[0x1234]!.Value.Type.HasValue);
            Assert.Equal(original.Changes[0x1234]!.Value.Type, tue.Changes[0x1234]!.Value.Type);
            Assert.Equal(original.Changes[0x1234]!.Value.Scenery, tue.Changes[0x1234]!.Value.Scenery);

        }
    }
}