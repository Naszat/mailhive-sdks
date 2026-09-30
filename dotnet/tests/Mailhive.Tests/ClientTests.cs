using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Mailhive.Tests;

public sealed class ClientTests
{
    private static readonly object Accepted = new { id = "m1", status = "queued", suppressed = Array.Empty<string>(), test = false };

    private static SendEmailRequest Email() => new() { From = "a@acme.com", To = "b@example.com", Subject = "s", Text = "t" };

    private static (MailhiveClient Client, FakeHandler Handler) ClientWith(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, MailhiveOptions? options = null)
    {
        var handler = new FakeHandler(respond);
        options ??= new MailhiveOptions();
        options.ApiKey ??= "mhs_x";
        var client = new MailhiveClient(options, new HttpClient(handler), _ => null) { Delay = (_, _) => Task.CompletedTask };
        return (client, handler);
    }

    private static (MailhiveClient Client, FakeHandler Handler) ClientWith(Func<HttpRequestMessage, HttpResponseMessage> respond, MailhiveOptions? options = null) =>
        ClientWith((request, _) => Task.FromResult(respond(request)), options);

    [Fact]
    public async Task ReadsTheKeyAndBaseUrlFromTheEnvironment()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json(200, Accepted));
        var environment = new Dictionary<string, string>
        {
            ["MAILHIVE_API_KEY"] = "mhs_fromenv",
            ["MAILHIVE_BASE_URL"] = "https://api-beta.mailhive.africa/v1/",
        };
        using var client = new MailhiveClient(null, new HttpClient(handler), name => environment.GetValueOrDefault(name));

        await client.Emails.SendAsync(Email());

        var request = handler.Requests.Single().Request;
        Assert.Equal("https://api-beta.mailhive.africa/v1/send/emails", request.RequestUri!.ToString());
        Assert.Equal("Bearer mhs_fromenv", request.Headers.GetValues("Authorization").Single());
        Assert.Equal("https://api-beta.mailhive.africa/v1", client.BaseUrl);
    }

    [Fact]
    public void AnExplicitKeyBeatsTheEnvironment()
    {
        var client = new MailhiveClient(new MailhiveOptions { ApiKey = "mhs_explicit" }, null, name => name == "MAILHIVE_API_KEY" ? "mhs_fromenv" : null);
        Assert.Equal(MailhiveClient.DefaultBaseUrl, client.BaseUrl);
        Assert.Equal(TimeSpan.FromSeconds(30), client.Timeout);
        Assert.Equal(2, client.MaxRetries);
    }

    [Fact]
    public void NeedsAKey()
    {
        var error = Assert.Throws<MailhiveException>(() => new MailhiveClient(null, null, _ => null));
        Assert.Contains("No API key", error.Message);
    }

    [Fact]
    public void RefusesAFormsPublishableKey()
    {
        var error = Assert.Throws<MailhiveException>(() => new MailhiveClient("mhp_form"));
        Assert.Equal(
            "That's a form's publishable key (mhp_…). The server SDK needs a secret API key (mhs_…) from Mailhive Send → API keys.",
            error.Message);
    }

    [Fact]
    public void NeverShowsTheKey()
    {
        using var client = new MailhiveClient("mhs_supersecretvalue");
        Assert.DoesNotContain("supersecret", client.ToString());
    }

    [Fact]
    public async Task SendsTheStandardHeadersAndOnlyTheFieldsThatAreSet()
    {
        var (client, handler) = ClientWith(_ => FakeHandler.Json(200, Accepted));
        var accepted = await client.Emails.SendAsync(new SendEmailRequest
        {
            From = "a@acme.com",
            To = new[] { "b@example.com", "c@example.com" },
            ReplyTo = "r@acme.com",
            TemplateId = "tpl_1",
            Variables = new Dictionary<string, object?> { ["name"] = "Ada", ["count"] = 3, ["vip"] = true, ["none"] = null },
            Tags = new Dictionary<string, string> { ["type"] = "receipt" },
        });

        Assert.Equal("m1", accepted.Id);
        var (request, body) = handler.Requests.Single();
        Assert.Matches(new Regex(@"^mailhive-dotnet/\d+\.\d+\.\d+ dotnet/"), request.Headers.UserAgent.ToString());
        Assert.Equal("application/json", request.Headers.Accept.Single().MediaType);
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        Assert.True(Guid.TryParse(request.Headers.GetValues("Idempotency-Key").Single(), out _));
        var json = JsonDocument.Parse(body!).RootElement;
        Assert.Equal(
            new[] { "from", "reply_to", "tags", "template_id", "to", "variables" },
            json.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(JsonValueKind.Array, json.GetProperty("to").ValueKind);
        Assert.Equal("r@acme.com", json.GetProperty("reply_to").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("variables").GetProperty("none").ValueKind);
        Assert.Equal(3, json.GetProperty("variables").GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task EncodesAttachments()
    {
        var (client, handler) = ClientWith(_ => FakeHandler.Json(200, Accepted));
        var email = Email();
        email.Attachments = new List<Attachment>
        {
            new("a.txt", "hello"u8.ToArray(), "text/plain"),
            Attachment.FromBase64("b.txt", "aGk="),
        };

        await client.Emails.SendAsync(email);

        var attachments = JsonDocument.Parse(handler.Requests.Single().Body!).RootElement.GetProperty("attachments");
        Assert.Equal(Convert.ToBase64String("hello"u8.ToArray()), attachments[0].GetProperty("content").GetString());
        Assert.Equal("text/plain", attachments[0].GetProperty("content_type").GetString());
        Assert.Equal("aGk=", attachments[1].GetProperty("content").GetString());
        Assert.False(attachments[1].TryGetProperty("content_type", out _));
    }

    [Fact]
    public void RecipientsReadAndWriteBothShapes()
    {
        Assert.Equal("\"a@x.com\"", JsonSerializer.Serialize<Recipients>("a@x.com"));
        Assert.Equal("[\"a@x.com\",\"b@x.com\"]", JsonSerializer.Serialize<Recipients>(new[] { "a@x.com", "b@x.com" }));
        Assert.Equal(new[] { "a@x.com" }, JsonSerializer.Deserialize<Recipients>("\"a@x.com\"")!);
        Assert.Equal(new[] { "a@x.com", "b@x.com" }, JsonSerializer.Deserialize<Recipients>("[\"a@x.com\",\"b@x.com\"]")!);
    }

    [Fact]
    public async Task GivesUpOnARetryAfterLongerThanAMinute()
    {
        var (client, handler) = ClientWith(_ =>
            FakeHandler.Json(429, new { error = new { code = "rate_limited", message = "slow" } }, ("Retry-After", "3600")));

        var error = await Assert.ThrowsAsync<RateLimitException>(() => client.Emails.GetAsync("m1"));

        Assert.Equal(TimeSpan.FromHours(1), error.RetryAfter);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ATimeoutIsAConnectionErrorAfterTheRetries()
    {
        var waits = new List<TimeSpan>();
        var (client, handler) = ClientWith(
            async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                throw new InvalidOperationException("unreachable");
            },
            new MailhiveOptions { Timeout = TimeSpan.FromMilliseconds(100) });
        client.Delay = (wait, _) =>
        {
            waits.Add(wait);
            return Task.CompletedTask;
        };

        var error = await Assert.ThrowsAsync<MailhiveConnectionException>(() => client.Emails.SendAsync(Email()));

        Assert.Equal("The Mailhive API didn't answer within 0.1 seconds.", error.Message);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Single(handler.Requests.Select(r => r.Request.Headers.GetValues("Idempotency-Key").Single()).Distinct());
        Assert.Equal(2, waits.Count);
        Assert.InRange(waits[0].TotalSeconds, 0.25, 0.5);
        Assert.InRange(waits[1].TotalSeconds, 0.5, 1.0);
    }

    [Fact]
    public async Task AnUnreachableHostIsAConnectionError()
    {
        var (client, _) = ClientWith(
            _ => throw new HttpRequestException("refused"),
            new MailhiveOptions { BaseUrl = "http://127.0.0.1:9/v1", MaxRetries = 0 });

        var error = await Assert.ThrowsAsync<MailhiveConnectionException>(() => client.Emails.GetAsync("m1"));

        Assert.Equal("Couldn't reach the Mailhive API at http://127.0.0.1:9/v1.", error.Message);
    }

    [Fact]
    public async Task TheCallersCancellationIsNotRetried()
    {
        using var cancellation = new CancellationTokenSource();
        var (client, handler) = ClientWith(async (_, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("unreachable");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.Emails.SendAsync(Email(), null, cancellation.Token));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ANonJsonErrorFallsBackToTheStatus()
    {
        var (client, _) = ClientWith(_ =>
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden) { Content = new StringContent("<html>no</html>") };
            response.Headers.Add("X-Request-Id", "req_header");
            return response;
        });

        var error = await Assert.ThrowsAsync<PermissionException>(() => client.Emails.GetAsync("m1"));

        Assert.Equal("http_error", error.Code);
        Assert.Equal("HTTP 403", error.Message);
        Assert.Equal("req_header", error.RequestId);
        Assert.Null(error.Details);
    }

    [Fact]
    public async Task EscapesTheIdAndSendsNoIdempotencyKeyOnGet()
    {
        var (client, handler) = ClientWith(_ => FakeHandler.Json(200, new { id = "a/b", status = "delivered", test = false, created_at = "2026-09-28T12:00:00" }));

        var email = await client.Emails.GetAsync("a/b");

        var request = handler.Requests.Single().Request;
        Assert.EndsWith("/send/emails/a%2Fb", request.RequestUri!.AbsoluteUri);
        Assert.False(request.Headers.Contains("Idempotency-Key"));
        Assert.Null(request.Content);
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero), email.CreatedAt);
    }

    [Fact]
    public void BackoffStaysWithinItsJitterWindow()
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var ceiling = Math.Min(8.0, 0.5 * Math.Pow(2, attempt));
            var wait = MailhiveClient.Backoff(attempt).TotalSeconds;
            Assert.InRange(wait, ceiling / 2, ceiling);
        }
    }

    [Fact]
    public void TheVersionMatchesThePackage()
    {
        var csproj = XDocument.Load(Path.Combine(Spec.RepoRoot, "dotnet", "src", "Mailhive", "Mailhive.csproj"));
        var version = csproj.Descendants("Version").Single().Value;
        Assert.Equal(version, MailhiveClient.Version);
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+"), MailhiveClient.Version);
    }
}
