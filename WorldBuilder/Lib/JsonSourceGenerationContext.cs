using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Services;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Lib {
    [JsonSourceGenerationOptions(WriteIndented = true, Converters = new[] { typeof(Vector3Converter) })]
    [JsonSerializable(typeof(WorldBuilderSettings))]
    [JsonSerializable(typeof(List<RecentProject>))]
    [JsonSerializable(typeof(RecentProject))]
    [JsonSerializable(typeof(LandscapeEditorSettings))]
    [JsonSerializable(typeof(AppSettings))]
    [JsonSerializable(typeof(CameraSettings))]
    [JsonSerializable(typeof(RenderingSettings))]
    [JsonSerializable(typeof(GridSettings))]
    [JsonSerializable(typeof(SelectionSettings))]
    [JsonSerializable(typeof(Vector3))]
    [JsonSerializable(typeof(DateTime))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(float))]
    [JsonSerializable(typeof(double))]
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(int))]
    [JsonSerializable(typeof(uint))]
    internal partial class SourceGenerationContext : JsonSerializerContext {
    }

    public class Vector3Converter : JsonConverter<Vector3> {
        public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            if (reader.TokenType != JsonTokenType.StartArray) {
                throw new JsonException("Expected start of array for Vector3.");
            }

            reader.Read();
            var x = reader.GetSingle();

            reader.Read();
            var y = reader.GetSingle();

            reader.Read();
            var z = reader.GetSingle();

            reader.Read();
            if (reader.TokenType != JsonTokenType.EndArray) {
                throw new JsonException("Expected end of array for Vector3.");
            }

            return new Vector3(x, y, z);
        }

        public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options) {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.X);
            writer.WriteNumberValue(value.Y);
            writer.WriteNumberValue(value.Z);
            writer.WriteEndArray();
        }
    }
}