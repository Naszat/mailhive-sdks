using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mailhive.Internal;

namespace Mailhive
{
    /// <summary>
    /// The Mailhive Send API client. Create one and reuse it (it's thread-safe), for example as a singleton.
    /// </summary>
    /// <example>
    /// <code>
    /// using var client = new MailhiveClient(); // reads MAILHIVE_API_KEY
    /// var email = await client.Emails.SendAsync(new SendEmailRequest
    /// {
    ///     From = "Acme &lt;hello@acme.com&gt;",
    ///     To = "ada@example.com",
    ///     Subject = "Your receipt",
    ///     Html = "&lt;p&gt;Thanks for your order.&lt;/p&gt;",
    /// });
    /// </code>
    /// </example>
    public sealed class MailhiveClient : IDisposable
    {
        /// <summary>The production API.</summary>
        public const string DefaultBaseUrl = "https://api.mailhive.africa/v1";

        private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);
        private static readonly Random Jitter = new Random();

        private readonly string _apiKey;
        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private readonly string _userAgent;

        /// <summary>Creates a client from <c>MAILHIVE_API_KEY</c> and <c>MAILHIVE_BASE_URL</c>.</summary>
        /// <exception cref="MailhiveException">No key is set, or it's a form's publishable key.</exception>
        public MailhiveClient() : this(null, null) { }

        /// <summary>Creates a client with a secret API key (<c>mhs_…</c>).</summary>
        /// <exception cref="MailhiveException">No key, or a form's publishable key.</exception>
        public MailhiveClient(string apiKey) : this(new MailhiveOptions { ApiKey = apiKey }) { }

        /// <summary>Creates a client.</summary>
        /// <param name="options">Settings; anything left unset comes from the environment or the defaults.</param>
        /// <param name="httpClient">
        /// An <see cref="HttpClient"/> to send through (for example from <c>IHttpClientFactory</c>). The client
        /// doesn't dispose it. When omitted, the client creates and owns one.
        /// </param>
        /// <exception cref="MailhiveException">No key, or a form's publishable key.</exception>
        public MailhiveClient(MailhiveOptions? options, HttpClient? httpClient = null)
            : this(options, httpClient, Environment.GetEnvironmentVariable) { }

        internal MailhiveClient(MailhiveOptions? options, HttpClient? httpClient, Func<string, string?> environment)
        {
            options ??= new MailhiveOptions();
            var key = NullIfEmpty(options.ApiKey) ?? NullIfEmpty(environment("MAILHIVE_API_KEY"));
            if (key is null)
            {
                throw new MailhiveException(
                    "No API key. Pass one to new MailhiveClient(...) or set MAILHIVE_API_KEY. Create keys under Mailhive Send → API keys.");
            }
            if (key.StartsWith("mhp_", StringComparison.Ordinal))
            {
                throw new MailhiveException(
                    "That's a form's publishable key (mhp_…). The server SDK needs a secret API key (mhs_…) from Mailhive Send → API keys.");
            }
            if (options.Timeout <= TimeSpan.Zero && options.Timeout != System.Threading.Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "Timeout must be positive (or Timeout.InfiniteTimeSpan).");
            }

            _apiKey = key;
            BaseUrl = (NullIfEmpty(options.BaseUrl) ?? NullIfEmpty(environment("MAILHIVE_BASE_URL")) ?? DefaultBaseUrl).TrimEnd('/');
            Timeout = options.Timeout;
            MaxRetries = Math.Max(0, options.MaxRetries);
            _userAgent = $"mailhive-dotnet/{Version} dotnet/{Environment.Version}";

            if (httpClient is null)
            {
                _http = new HttpClient(CreateHandler()) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
                _ownsHttp = true;
            }
            else
            {
                _http = httpClient;
            }
            Emails = new EmailsResource(this);
        }

        /// <summary>This SDK's version.</summary>
        public static string Version { get; } = ReadVersion();

        /// <summary>The API's base URL, without a trailing slash.</summary>
        public string BaseUrl { get; }

        /// <summary>How long one attempt may take.</summary>
        public TimeSpan Timeout { get; }

        /// <summary>How many times a failed request is retried.</summary>
        public int MaxRetries { get; }

        /// <summary>Sends and looks up emails.</summary>
        public EmailsResource Emails { get; }

        /// <summary>Waits between retries. Tests replace it to run without real delays.</summary>
        internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = (wait, token) => Task.Delay(wait, token);

        /// <summary>Never includes the API key.</summary>
        public override string ToString() => $"MailhiveClient(BaseUrl={BaseUrl})";

        /// <summary>Releases the <see cref="HttpClient"/>, if the client created it.</summary>
        public void Dispose()
        {
            if (_ownsHttp)
            {
                _http.Dispose();
            }
        }

        internal async Task<T> RequestAsync<T>(
            HttpMethod method, string path, object? body, string? idempotencyKey, CancellationToken cancellationToken)
        {
            var content = body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), Json.Options);
            // One key per call, reused on every retry: a retry never sends twice.
            var key = method == HttpMethod.Post ? idempotencyKey ?? Guid.NewGuid().ToString() : null;
            var url = BaseUrl + path;

            for (var attempt = 0; ; attempt++)
            {
                int status;
                string text;
                IReadOnlyDictionary<string, string> headers;
                using (var request = BuildRequest(method, url, content, key))
                using (var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    if (Timeout != System.Threading.Timeout.InfiniteTimeSpan)
                    {
                        attemptCts.CancelAfter(Timeout);
                    }
                    try
                    {
                        using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, attemptCts.Token).ConfigureAwait(false))
                        {
                            text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            status = (int)response.StatusCode;
                            headers = CollectHeaders(response);
                        }
                    }
                    catch (Exception exception) when (IsTransportFailure(exception, cancellationToken))
                    {
                        if (attempt < MaxRetries)
                        {
                            await Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                            continue;
                        }
                        throw new MailhiveConnectionException(
                            exception is OperationCanceledException
                                ? $"The Mailhive API didn't answer within {Timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} seconds."
                                : $"Couldn't reach the Mailhive API at {BaseUrl}.",
                            exception);
                    }
                }

                if (status >= 200 && status < 300)
                {
                    try
                    {
                        return JsonSerializer.Deserialize<T>(text, Json.Options)
                            ?? throw new JsonException("The response body was null.");
                    }
                    catch (JsonException exception)
                    {
                        throw new MailhiveException($"The Mailhive API sent a response that isn't the expected JSON (HTTP {status}).", exception);
                    }
                }

                var error = ParseError(status, text, headers);
                var wait = WaitBeforeRetry(error, attempt);
                if (wait is null)
                {
                    throw error;
                }
                await Delay(wait.Value, cancellationToken).ConfigureAwait(false);
            }
        }

        private HttpRequestMessage BuildRequest(HttpMethod method, string url, byte[]? content, string? idempotencyKey)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _apiKey);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
            if (idempotencyKey != null)
            {
                request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
            }
            if (content != null)
            {
                request.Content = new ByteArrayContent(content);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }
            return request;
        }

        /// <summary>How long to wait before retrying, or null to give up. Only a rate limit and server errors are worth retrying.</summary>
        private TimeSpan? WaitBeforeRetry(MailhiveApiException error, int attempt)
        {
            if (attempt >= MaxRetries)
            {
                return null;
            }
            // A used-up allowance or a bad request fails the same way again.
            if (!(error.StatusCode >= 500 || (error.StatusCode == 429 && error.Code == "rate_limited")))
            {
                return null;
            }
            var retryAfter = error.RetryAfterHeader();
            if (retryAfter is null)
            {
                return Backoff(attempt);
            }
            return retryAfter.Value > MaxRetryAfter ? (TimeSpan?)null : retryAfter.Value;
        }

        private static MailhiveApiException ParseError(int status, string text, IReadOnlyDictionary<string, string> headers)
        {
            string? message = null, code = null, requestId = null;
            JsonElement? details = null;
            try
            {
                using (var document = JsonDocument.Parse(text))
                {
                    if (document.RootElement.ValueKind == JsonValueKind.Object
                        && document.RootElement.TryGetProperty("error", out var error)
                        && error.ValueKind == JsonValueKind.Object)
                    {
                        message = StringProperty(error, "message");
                        code = StringProperty(error, "code");
                        requestId = StringProperty(error, "request_id");
                        if (error.TryGetProperty("details", out var found)
                            && found.ValueKind != JsonValueKind.Null && found.ValueKind != JsonValueKind.Undefined)
                        {
                            details = found.Clone();
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // A proxy's HTML error page, for example: fall back to the status.
            }
            if (requestId is null)
            {
                headers.TryGetValue("X-Request-Id", out requestId);
            }
            return MailhiveApiException.Create(message ?? $"HTTP {status}", status, code ?? "http_error", details, requestId, headers);
        }

        private static string? StringProperty(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private static IReadOnlyDictionary<string, string> CollectHeaders(HttpResponseMessage response)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in response.Headers.Concat(response.Content.Headers))
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }
            return headers;
        }

        private static bool IsTransportFailure(Exception exception, CancellationToken callerToken) =>
            exception is HttpRequestException
            || exception is IOException
            // A timeout (ours or the HttpClient's), not the caller cancelling.
            || (exception is OperationCanceledException && !callerToken.IsCancellationRequested);

        /// <summary>About 0.5s, 1s, 2s … up to 8s, with jitter: uniform in [ceiling/2, ceiling].</summary>
        internal static TimeSpan Backoff(int attempt)
        {
            var ceiling = Math.Min(8.0, 0.5 * Math.Pow(2, attempt));
            double random;
            lock (Jitter)
            {
                random = Jitter.NextDouble();
            }
            return TimeSpan.FromSeconds(ceiling / 2 + random * ceiling / 2);
        }

        private static HttpMessageHandler CreateHandler()
        {
#if NET
            // Recycles connections so DNS changes are picked up by a long-lived client.
            return new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
#else
            return new HttpClientHandler();
#endif
        }

        private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

        private static string ReadVersion()
        {
            var informational = typeof(MailhiveClient).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(informational))
            {
                return informational!.Split('+')[0];
            }
            var version = typeof(MailhiveClient).Assembly.GetName().Version;
            return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }
}
