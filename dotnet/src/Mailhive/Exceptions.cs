using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Mailhive
{
    /// <summary>The base class of every exception this SDK throws.</summary>
    public class MailhiveException : Exception
    {
        /// <summary>Creates the exception.</summary>
        public MailhiveException(string message) : base(message) { }

        /// <summary>Creates the exception with the error that caused it.</summary>
        public MailhiveException(string message, Exception? innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// An error response from the API. Check <see cref="Code"/> (stable), not <see cref="Exception.Message"/>
    /// (may change), and quote <see cref="RequestId"/> to support.
    /// </summary>
    public class MailhiveApiException : MailhiveException
    {
        private static readonly IReadOnlyDictionary<string, string> NoHeaders =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Creates the exception.</summary>
        public MailhiveApiException(
            string message,
            int statusCode,
            string code,
            JsonElement? details = null,
            string? requestId = null,
            IReadOnlyDictionary<string, string>? headers = null)
            : base(message)
        {
            StatusCode = statusCode;
            Code = code;
            Details = details;
            RequestId = requestId;
            Headers = headers ?? NoHeaders;
        }

        /// <summary>The HTTP status code.</summary>
        public int StatusCode { get; }

        /// <summary>Mailhive's stable, machine-readable error code, such as <c>validation_error</c>, or <c>http_error</c> when the response had none.</summary>
        public string Code { get; }

        /// <summary>Extra information. For <c>validation_error</c>, the list of problems (<c>type</c>, <c>loc</c>, <c>msg</c>).</summary>
        public JsonElement? Details { get; }

        /// <summary>Identifies the request. Quote it when contacting support.</summary>
        public string? RequestId { get; }

        /// <summary>The response headers (case-insensitive names).</summary>
        public IReadOnlyDictionary<string, string> Headers { get; }

        /// <inheritdoc />
        public override string ToString() =>
            $"{GetType().FullName}: {Message} (status {StatusCode}, code {Code}, request id {RequestId ?? "none"})";

        internal static MailhiveApiException Create(
            string message, int status, string code, JsonElement? details, string? requestId, IReadOnlyDictionary<string, string> headers)
        {
            switch (status)
            {
                case 401: return new AuthenticationException(message, status, code, details, requestId, headers);
                case 402: return new BillingException(message, status, code, details, requestId, headers);
                case 403: return new PermissionException(message, status, code, details, requestId, headers);
                case 404: return new NotFoundException(message, status, code, details, requestId, headers);
                case 409: return new ConflictException(message, status, code, details, requestId, headers);
                case 400:
                case 422: return new ValidationException(message, status, code, details, requestId, headers);
                case 429: return new RateLimitException(message, status, code, details, requestId, headers);
                default: return new MailhiveApiException(message, status, code, details, requestId, headers);
            }
        }

        /// <summary>The <c>Retry-After</c> header in seconds, when it's a non-negative number.</summary>
        internal TimeSpan? RetryAfterHeader()
        {
            if (!Headers.TryGetValue("Retry-After", out var value) || string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                && seconds >= 0 && !double.IsInfinity(seconds))
            {
                return TimeSpan.FromSeconds(seconds);
            }
            return null;
        }
    }

    /// <summary>401: the API key is missing, unknown or revoked.</summary>
    public class AuthenticationException : MailhiveApiException
    {
        /// <summary>Creates the exception.</summary>
        public AuthenticationException(string message, int statusCode, string code, JsonElement? details = null, string? requestId = null, IReadOnlyDictionary<string, string>? headers = null)
            : base(message, statusCode, code, details, requestId, headers) { }
    }

    /// <summary>402: Mailhive Send is paused over an unpaid invoice.</summary>
    public class BillingException : MailhiveApiException
    {
        /// <summary>Creates the exception.</summary>
        public BillingException(string message, int statusCode, string code, JsonElement? details = null, string? requestId = null, IReadOnlyDictionary<string, string>? headers = null)
            : base(message, statusCode, code, details, requestId, headers) { }
    }

    /// <summary>403: Send isn't activated, or the stream is paused.</summary>
    public class PermissionException : MailhiveApiException
    {
        /// <summary>Creates the exception.</summary>
        public PermissionException(string message, int statusCode, string code, JsonElement? details = null, string? requestId = null, IReadOnlyDictionary<string, string>? headers = null)
            : base(message, statusCode, code, details, requestId, headers) { }
    }

    /// <summary>404: the email (or route) doesn't exist.</summary>
    public class NotFoundException : MailhiveApiException
    {
        /// <summary>Creates the exception.</summary>
        public NotFoundException(string message, int statusCode, string code, JsonElement? details = null, string? requestId = null, IReadOnlyDictionary<string, string>? headers = null)
            : base(message, statusCode, code, details, requestId, headers) { }
    }

    /// <summary>409: for example an <c>Idempotency-Key</c> reused for a different request (<c>idempotency_conflict</c>).</summary>
    public class ConflictException : MailhiveApiException
    {
        /// <summary>Creates the exception.</summary>
        public ConflictException(string message, int statusCode, string code, JsonElement? details = null, string? requestId = null, IReadOnlyDictionary<string, string>? headers = null)
            : base(message, statusCode, code, details, requestId, headers) { }
    }

    /// <summary>400 or 422: the request is invalid. <see cref="MailhiveApiException.Details"/> lists every problem.</summary>
    public class ValidationException : MailhiveApiException
    {
        /// <summary>Creates the exception.</summary>
        public ValidationException(string message, int statusCode, string code, JsonElement? details = null, string? requestId = null, IReadOnlyDictionary<string, string>? headers = null)
            : base(message, statusCode, code, details, requestId, headers) { }
    }

    /// <summary>
    /// 429: <c>rate_limited</c> (retried for you), or <c>monthly_quota_reached</c> / <c>daily_cap_reached</c>
    /// (not retried, because waiting won't help).
    /// </summary>
    public class RateLimitException : MailhiveApiException
    {
        /// <summary>Creates the exception.</summary>
        public RateLimitException(string message, int statusCode, string code, JsonElement? details = null, string? requestId = null, IReadOnlyDictionary<string, string>? headers = null)
            : base(message, statusCode, code, details, requestId, headers) { }

        /// <summary>How long the API asked to wait (<c>Retry-After</c>), when it said.</summary>
        public TimeSpan? RetryAfter => RetryAfterHeader();
    }

    /// <summary>The API couldn't be reached, or didn't answer in time, even after the retries.</summary>
    public class MailhiveConnectionException : MailhiveException
    {
        /// <summary>Creates the exception.</summary>
        public MailhiveConnectionException(string message, Exception? innerException = null) : base(message, innerException) { }
    }

    /// <summary>A webhook's <c>Mailhive-Signature</c> didn't check out. Answer the request with 400.</summary>
    public class WebhookVerificationException : MailhiveException
    {
        /// <summary>Creates the exception.</summary>
        public WebhookVerificationException(string message, string reason) : base(message)
        {
            Reason = reason;
        }

        /// <summary>
        /// Why verification failed: <c>header</c> (missing or malformed header), <c>timestamp</c> (outside the
        /// tolerance, so possibly a replay) or <c>signature</c> (wrong secret, or the body was changed).
        /// </summary>
        public string Reason { get; }
    }
}
