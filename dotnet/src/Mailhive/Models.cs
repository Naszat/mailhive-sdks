using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using Mailhive.Internal;

namespace Mailhive
{
    /// <summary>
    /// One email to send. The JSON names match the <see href="https://mailhive.africa/docs/api/send-email">API reference</see>
    /// exactly; properties left <c>null</c> aren't sent. Give either <see cref="Html"/>/<see cref="Text"/> or a
    /// <see cref="TemplateId"/>.
    /// </summary>
    public sealed class SendEmailRequest
    {
        /// <summary>Required. The sender, at a verified sending domain: <c>hello@acme.com</c> or <c>Acme &lt;hello@acme.com&gt;</c>.</summary>
        [JsonPropertyName("from")]
        public string From { get; set; } = string.Empty;

        /// <summary>Required. One recipient or several (at most 50 across <c>to</c>, <c>cc</c> and <c>bcc</c>).</summary>
        [JsonPropertyName("to")]
        public Recipients To { get; set; } = new Recipients();

        /// <summary>Copied recipients.</summary>
        [JsonPropertyName("cc")]
        public Recipients? Cc { get; set; }

        /// <summary>Hidden recipients.</summary>
        [JsonPropertyName("bcc")]
        public Recipients? Bcc { get; set; }

        /// <summary>The subject line (optional with a template).</summary>
        [JsonPropertyName("subject")]
        public string? Subject { get; set; }

        /// <summary>The HTML body.</summary>
        [JsonPropertyName("html")]
        public string? Html { get; set; }

        /// <summary>The plain-text body.</summary>
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        /// <summary>A template to render instead of <see cref="Html"/>/<see cref="Text"/>.</summary>
        [JsonPropertyName("template_id")]
        public string? TemplateId { get; set; }

        /// <summary>Values for the template's variables: strings, numbers, booleans or <c>null</c>.</summary>
        [JsonPropertyName("variables")]
        public IDictionary<string, object?>? Variables { get; set; }

        /// <summary>Where replies go.</summary>
        [JsonPropertyName("reply_to")]
        public Recipients? ReplyTo { get; set; }

        /// <summary>Extra email headers.</summary>
        [JsonPropertyName("headers")]
        public IDictionary<string, string>? Headers { get; set; }

        /// <summary>Up to 10 of your own labels, returned in webhook events.</summary>
        [JsonPropertyName("tags")]
        public IDictionary<string, string>? Tags { get; set; }

        /// <summary>Files to attach.</summary>
        [JsonPropertyName("attachments")]
        public IList<Attachment>? Attachments { get; set; }
    }

    /// <summary>A file attached to an email.</summary>
    public sealed class Attachment
    {
        /// <summary>Creates an empty attachment; set <see cref="Filename"/> and <see cref="Content"/> (base64).</summary>
        public Attachment() { }

        /// <summary>Attaches <paramref name="content"/>, which is base64-encoded for you.</summary>
        public Attachment(string filename, byte[] content, string? contentType = null)
        {
            Filename = filename ?? throw new ArgumentNullException(nameof(filename));
            Content = Convert.ToBase64String(content ?? throw new ArgumentNullException(nameof(content)));
            ContentType = contentType;
        }

        /// <summary>An attachment whose content is already base64 text.</summary>
        public static Attachment FromBase64(string filename, string base64Content, string? contentType = null) =>
            new Attachment
            {
                Filename = filename ?? throw new ArgumentNullException(nameof(filename)),
                Content = base64Content ?? throw new ArgumentNullException(nameof(base64Content)),
                ContentType = contentType,
            };

        /// <summary>Reads a file from disk. The attachment is named after the file.</summary>
        public static Attachment FromFile(string path, string? contentType = null) =>
            new Attachment(Path.GetFileName(path), File.ReadAllBytes(path), contentType);

        /// <summary>The name shown to the recipient.</summary>
        [JsonPropertyName("filename")]
        public string Filename { get; set; } = string.Empty;

        /// <summary>The file, base64-encoded.</summary>
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        /// <summary>The MIME type. The API defaults to <c>application/octet-stream</c>.</summary>
        [JsonPropertyName("content_type")]
        public string? ContentType { get; set; }
    }

    /// <summary>An email the API accepted.</summary>
    public sealed class AcceptedEmail
    {
        /// <summary>The email's id, for <see cref="EmailsResource.GetAsync"/> and webhook events.</summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary><c>queued</c>, or <c>suppressed</c> when no recipient was left after the suppression list and the recipient checks.</summary>
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        /// <summary>Recipients dropped because they're on the suppression list.</summary>
        [JsonPropertyName("suppressed")]
        public IReadOnlyList<string> Suppressed { get; set; } = Array.Empty<string>();

        /// <summary>Recipients dropped before sending because the address can't work, keyed by address: <c>invalid_syntax</c>, <c>domain_typo</c> (gmial.com), <c>domain_not_found</c> or <c>no_mail_server</c>. They never reach the sending engine, so they can't bounce.</summary>
        [JsonPropertyName("rejected")]
        public IReadOnlyDictionary<string, string> Rejected { get; set; } = new Dictionary<string, string>();

        /// <summary>True when sent with a test key (<c>mhs_test_…</c>): delivery is simulated and nothing is billed.</summary>
        [JsonPropertyName("test")]
        public bool Test { get; set; }
    }

    /// <summary>What <see cref="EmailsResource.ValidateAsync"/> found for one address.</summary>
    public sealed class RecipientVerdict
    {
        /// <summary>The address as given.</summary>
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        /// <summary>The bare, lowercased address, or <c>null</c> when there isn't one.</summary>
        [JsonPropertyName("normalized")]
        public string? Normalized { get; set; }

        /// <summary>False when sending to this address would be refused.</summary>
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        /// <summary>Why it's invalid: <c>invalid_syntax</c>, <c>domain_typo</c>, <c>domain_not_found</c>, <c>no_mail_server</c> or <c>suppressed</c> (on your suppression list). <c>null</c> when valid.</summary>
        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        /// <summary>The reason in plain words, fit to show the person typing the address.</summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        /// <summary>For <c>domain_typo</c>, the address that was probably meant (ada@gmail.com for ada@gmial.com).</summary>
        [JsonPropertyName("suggestion")]
        public string? Suggestion { get; set; }
    }

    /// <summary>An email and where it's got to.</summary>
    public sealed class Email
    {
        /// <summary>The email's id.</summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary><c>queued</c>, <c>sent</c>, <c>delivered</c>, <c>delayed</c>, <c>bounced</c>, <c>complained</c>, <c>suppressed</c> or <c>failed</c>.</summary>
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        /// <summary><c>transactional</c> or <c>broadcast</c>.</summary>
        [JsonPropertyName("stream")]
        public string? Stream { get; set; }

        /// <summary>True when sent with a test key: delivery was simulated.</summary>
        [JsonPropertyName("test")]
        public bool Test { get; set; }

        /// <summary>The sender.</summary>
        [JsonPropertyName("from")]
        public string? From { get; set; }

        /// <summary>The recipients.</summary>
        [JsonPropertyName("to")]
        public IReadOnlyList<string> To { get; set; } = Array.Empty<string>();

        /// <summary>The copied recipients.</summary>
        [JsonPropertyName("cc")]
        public IReadOnlyList<string> Cc { get; set; } = Array.Empty<string>();

        /// <summary>The hidden recipients.</summary>
        [JsonPropertyName("bcc")]
        public IReadOnlyList<string> Bcc { get; set; } = Array.Empty<string>();

        /// <summary>The subject line.</summary>
        [JsonPropertyName("subject")]
        public string? Subject { get; set; }

        /// <summary>Recipients dropped because they're on the suppression list.</summary>
        [JsonPropertyName("suppressed")]
        public IReadOnlyList<string> Suppressed { get; set; } = Array.Empty<string>();

        /// <summary>Recipients dropped before sending because the address can't work, keyed by address: <c>invalid_syntax</c>, <c>domain_typo</c> (gmial.com), <c>domain_not_found</c> or <c>no_mail_server</c>. They never reach the sending engine, so they can't bounce.</summary>
        [JsonPropertyName("rejected")]
        public IReadOnlyDictionary<string, string> Rejected { get; set; } = new Dictionary<string, string>();

        /// <summary>Your labels.</summary>
        [JsonPropertyName("tags")]
        public IReadOnlyDictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        /// <summary>The template used, if any.</summary>
        [JsonPropertyName("template_id")]
        public string? TemplateId { get; set; }

        /// <summary>The template's version, if a template was used.</summary>
        [JsonPropertyName("template_version")]
        public int? TemplateVersion { get; set; }

        /// <summary>When the API accepted the email.</summary>
        [JsonPropertyName("created_at")]
        [JsonConverter(typeof(UtcDateTimeOffsetConverter))]
        public DateTimeOffset? CreatedAt { get; set; }

        /// <summary>When it was handed to the recipient's server.</summary>
        [JsonPropertyName("sent_at")]
        [JsonConverter(typeof(UtcDateTimeOffsetConverter))]
        public DateTimeOffset? SentAt { get; set; }

        /// <summary>When the latest event happened.</summary>
        [JsonPropertyName("last_event_at")]
        [JsonConverter(typeof(UtcDateTimeOffsetConverter))]
        public DateTimeOffset? LastEventAt { get; set; }
    }
}
