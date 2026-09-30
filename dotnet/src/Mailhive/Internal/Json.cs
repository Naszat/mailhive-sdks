using System;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mailhive.Internal
{
    internal static class Json
    {
        /// <summary>Leaves out nulls, and doesn't escape HTML (the body goes to an API, not into a page).</summary>
        internal static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
    }

    /// <summary>Reads timestamps; one without an offset is taken as UTC, as the API means it.</summary>
    internal sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
    {
        public override bool HandleNull => true;

        public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }
            return Parse(reader.GetString());
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStringValue(value.Value.ToString("O", CultureInfo.InvariantCulture));
            }
        }

        internal static DateTimeOffset? Parse(string? value) =>
            DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed)
                ? parsed
                : (DateTimeOffset?)null;
    }
}
