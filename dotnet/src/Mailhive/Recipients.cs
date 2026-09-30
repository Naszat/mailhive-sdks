using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mailhive
{
    /// <summary>
    /// One or more email addresses, each <c>ada@example.com</c> or <c>Ada &lt;ada@example.com&gt;</c>.
    /// Converts implicitly from a <see cref="string"/>, a <c>string[]</c> or a <c>List&lt;string&gt;</c>.
    /// A single address is sent as a JSON string, several as an array.
    /// </summary>
    [JsonConverter(typeof(RecipientsJsonConverter))]
    public sealed class Recipients : IReadOnlyList<string>
    {
        private readonly string[] _addresses;

        /// <summary>Creates a list of recipients.</summary>
        public Recipients(params string[] addresses)
            : this((IEnumerable<string>)(addresses ?? throw new ArgumentNullException(nameof(addresses))))
        {
        }

        /// <summary>Creates a list of recipients.</summary>
        public Recipients(IEnumerable<string> addresses)
        {
            if (addresses is null)
            {
                throw new ArgumentNullException(nameof(addresses));
            }
            _addresses = addresses.ToArray();
            if (_addresses.Any(a => a is null))
            {
                throw new ArgumentException("An address can't be null.", nameof(addresses));
            }
        }

        /// <inheritdoc />
        public int Count => _addresses.Length;

        /// <inheritdoc />
        public string this[int index] => _addresses[index];

        /// <inheritdoc />
        public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)_addresses).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _addresses.GetEnumerator();

        /// <summary>One recipient.</summary>
        public static implicit operator Recipients(string address) => new Recipients(address);

        /// <summary>Several recipients.</summary>
        public static implicit operator Recipients(string[] addresses) => new Recipients(addresses);

        /// <summary>Several recipients.</summary>
        public static implicit operator Recipients(List<string> addresses) => new Recipients(addresses);

        /// <summary>The addresses, comma-separated.</summary>
        public override string ToString() => string.Join(", ", _addresses);
    }

    internal sealed class RecipientsJsonConverter : JsonConverter<Recipients>
    {
        public override Recipients? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return null;
                case JsonTokenType.String:
                    return new Recipients(reader.GetString()!);
                case JsonTokenType.StartArray:
                    var addresses = new List<string>();
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType != JsonTokenType.String)
                        {
                            throw new JsonException("Recipients must be strings.");
                        }
                        addresses.Add(reader.GetString()!);
                    }
                    return new Recipients(addresses);
                default:
                    throw new JsonException("Recipients must be a string or an array of strings.");
            }
        }

        public override void Write(Utf8JsonWriter writer, Recipients value, JsonSerializerOptions options)
        {
            if (value.Count == 1)
            {
                writer.WriteStringValue(value[0]);
                return;
            }
            writer.WriteStartArray();
            foreach (var address in value)
            {
                writer.WriteStringValue(address);
            }
            writer.WriteEndArray();
        }
    }
}
