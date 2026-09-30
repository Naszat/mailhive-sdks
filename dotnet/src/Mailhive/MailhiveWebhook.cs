using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mailhive.Internal;

namespace Mailhive
{
    /// <summary>A verified webhook event.</summary>
    public sealed class WebhookEvent
    {
        internal WebhookEvent(string type, DateTimeOffset? createdAt, bool test, JsonElement data, JsonElement raw)
        {
            Type = type;
            CreatedAt = createdAt;
            Test = test;
            Data = data;
            Raw = raw;
        }

        /// <summary>For example <c>email.delivered</c>, <c>email.bounced</c> or <c>contact.unsubscribed</c>.</summary>
        public string Type { get; }

        /// <summary>When the event happened.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>True for events that aren't real: test events from the dashboard, and mail sent with a test key.</summary>
        public bool Test { get; }

        /// <summary>The event's <c>data</c> object, such as <c>email_id</c>, <c>to</c> and <c>tags</c>.</summary>
        public JsonElement Data { get; }

        /// <summary>The whole event, as sent.</summary>
        public JsonElement Raw { get; }
    }

    /// <summary>Checks the <c>Mailhive-Signature</c> header on webhook requests.</summary>
    public static class MailhiveWebhook
    {
        /// <summary>The header that carries the signature.</summary>
        public const string SignatureHeader = "Mailhive-Signature";

        /// <summary>How far the signature's timestamp may be from now: 300 seconds.</summary>
        public static readonly TimeSpan DefaultTolerance = TimeSpan.FromSeconds(300);

        /// <summary>
        /// Checks a webhook's <c>Mailhive-Signature</c> header (HMAC-SHA256 of <c>"&lt;t&gt;.&lt;raw body&gt;"</c>
        /// with the endpoint's signing secret) and returns the event. <paramref name="payload"/> must be the raw
        /// body exactly as received: parsing and re-serializing it changes the bytes. Several <c>v1=</c> values
        /// are accepted, so secrets can be rotated.
        /// </summary>
        /// <param name="payload">The raw request body.</param>
        /// <param name="signatureHeader">The <c>Mailhive-Signature</c> header's value.</param>
        /// <param name="secret">The endpoint's signing secret (<c>whsec_…</c>).</param>
        /// <param name="tolerance">How far the timestamp may be from now. Defaults to 300 seconds.</param>
        /// <param name="now">The time to check against. Defaults to the current time.</param>
        /// <exception cref="WebhookVerificationException">The signature didn't check out; see <see cref="WebhookVerificationException.Reason"/>.</exception>
        public static WebhookEvent Verify(
            string payload, string? signatureHeader, string secret, TimeSpan? tolerance = null, DateTimeOffset? now = null)
        {
            if (payload is null)
            {
                throw new ArgumentNullException(nameof(payload));
            }
            return Verify(Encoding.UTF8.GetBytes(payload), signatureHeader, secret, tolerance, now);
        }

        /// <inheritdoc cref="Verify(string, string?, string, TimeSpan?, DateTimeOffset?)"/>
        public static WebhookEvent Verify(
            byte[] payload, string? signatureHeader, string secret, TimeSpan? tolerance = null, DateTimeOffset? now = null)
        {
            if (payload is null)
            {
                throw new ArgumentNullException(nameof(payload));
            }
            if (secret is null)
            {
                throw new ArgumentNullException(nameof(secret));
            }
            var limit = tolerance ?? DefaultTolerance;

            if (!TryParseHeader(signatureHeader, out var timestamp, out var candidates))
            {
                throw new WebhookVerificationException("Missing or malformed Mailhive-Signature header.", "header");
            }
            var current = (now ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds() / 1000.0;
            if (Math.Abs(current - timestamp) > limit.TotalSeconds)
            {
                throw new WebhookVerificationException(
                    $"The webhook's timestamp is more than {limit.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} seconds from now; it may be a replay.",
                    "timestamp");
            }

            var prefix = Encoding.UTF8.GetBytes(timestamp.ToString(CultureInfo.InvariantCulture) + ".");
            var signed = new byte[prefix.Length + payload.Length];
            Buffer.BlockCopy(prefix, 0, signed, 0, prefix.Length);
            Buffer.BlockCopy(payload, 0, signed, prefix.Length, payload.Length);
            byte[] expected;
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
            {
                expected = Encoding.ASCII.GetBytes(ToHex(hmac.ComputeHash(signed)));
            }

            var matched = false;
            foreach (var candidate in candidates)
            {
                matched |= FixedTimeEquals(expected, Encoding.ASCII.GetBytes(candidate));
            }
            if (!matched)
            {
                throw new WebhookVerificationException(
                    "The webhook's signature doesn't match. Check the endpoint's signing secret.", "signature");
            }
            return Parse(payload);
        }

        private static bool TryParseHeader(string? header, out long timestamp, out List<string> signatures)
        {
            timestamp = 0;
            signatures = new List<string>();
            var found = false;
            if (string.IsNullOrEmpty(header))
            {
                return false;
            }
            foreach (var part in header!.Split(','))
            {
                var separator = part.IndexOf('=');
                if (separator < 0)
                {
                    continue;
                }
                var key = part.Substring(0, separator).Trim();
                var value = part.Substring(separator + 1).Trim();
                if (key == "t" && IsDigits(value) && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                {
                    timestamp = parsed;
                    found = true;
                }
                else if (key == "v1" && IsHex64(value))
                {
                    signatures.Add(value.ToLowerInvariant());
                }
            }
            return found && signatures.Count > 0;
        }

        private static WebhookEvent Parse(byte[] payload)
        {
            using (var document = JsonDocument.Parse(payload))
            {
                var root = document.RootElement.Clone();
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonException("A webhook event must be a JSON object.");
                }
                var type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : string.Empty;
                var createdAt = root.TryGetProperty("created_at", out var c) && c.ValueKind == JsonValueKind.String
                    ? UtcDateTimeOffsetConverter.Parse(c.GetString())
                    : null;
                var test = root.TryGetProperty("test", out var flag) && flag.ValueKind == JsonValueKind.True;
                var data = root.TryGetProperty("data", out var d) ? d : default;
                return new WebhookEvent(type, createdAt, test, data, root);
            }
        }

        private static bool IsDigits(string value)
        {
            if (value.Length == 0)
            {
                return false;
            }
            foreach (var ch in value)
            {
                if (ch < '0' || ch > '9')
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsHex64(string value)
        {
            if (value.Length != 64)
            {
                return false;
            }
            foreach (var ch in value)
            {
                if (!((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F')))
                {
                    return false;
                }
            }
            return true;
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
            {
                builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
#if NET
            return CryptographicOperations.FixedTimeEquals(left, right);
#else
            if (left.Length != right.Length)
            {
                return false;
            }
            var difference = 0;
            for (var i = 0; i < left.Length; i++)
            {
                difference |= left[i] ^ right[i];
            }
            return difference == 0;
#endif
        }
    }
}
