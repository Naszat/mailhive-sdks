using System;

namespace Mailhive
{
    /// <summary>Settings for <see cref="MailhiveClient"/>. Every property is optional.</summary>
    public sealed class MailhiveOptions
    {
        /// <summary>
        /// A secret API key (<c>mhs_…</c>, or <c>mhs_test_…</c> for simulated delivery). Defaults to the
        /// <c>MAILHIVE_API_KEY</c> environment variable. Create keys under Mailhive Send → API keys.
        /// </summary>
        public string? ApiKey { get; set; }

        /// <summary>
        /// The API's base URL. Defaults to the <c>MAILHIVE_BASE_URL</c> environment variable, then
        /// <see cref="MailhiveClient.DefaultBaseUrl"/>. Set <c>https://api-beta.mailhive.africa/v1</c> for beta.
        /// </summary>
        public string? BaseUrl { get; set; }

        /// <summary>How long one attempt may take before it counts as a timeout. Defaults to 30 seconds.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// How many times a failed request is retried (network errors, timeouts, 5xx and <c>429 rate_limited</c>).
        /// Defaults to 2. Retries reuse the same <c>Idempotency-Key</c>, so they never send twice.
        /// </summary>
        public int MaxRetries { get; set; } = 2;
    }

    /// <summary>Per-call options for a send.</summary>
    public sealed class SendOptions
    {
        /// <summary>
        /// Makes the send safe to repeat: the API accepts a key once, so a repeated call returns the first result
        /// instead of sending again. Without one, the SDK generates a key per call (reused on its own retries).
        /// Pass your own, such as <c>order-1042-receipt</c>, to stay safe across restarts.
        /// </summary>
        public string? IdempotencyKey { get; set; }
    }
}
