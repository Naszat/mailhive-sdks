using System.Text.Json;
using Xunit;

namespace Mailhive.Tests;

/// <summary>spec/webhook-vectors.json: signatures made with the Mailhive backend's own signing function.</summary>
public sealed class WebhookTests
{
    private static readonly JsonElement Vectors = Spec.Load("webhook-vectors.json");

    public static IEnumerable<object[]> Cases() =>
        Vectors.GetProperty("cases").EnumerateArray().Select(c => new object[] { c.GetProperty("name").GetString()! });

    [Theory]
    [MemberData(nameof(Cases))]
    public void MatchesTheBackend(string name)
    {
        var vector = Vectors.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("name").GetString() == name);
        var payload = vector.GetProperty("payload").GetString()!;
        var header = vector.GetProperty("header").GetString();
        var secret = vector.GetProperty("secret").GetString()!;
        var now = DateTimeOffset.FromUnixTimeSeconds(vector.GetProperty("now").GetInt64());
        var tolerance = TimeSpan.FromSeconds(Vectors.GetProperty("tolerance_seconds").GetInt32());

        if (vector.GetProperty("valid").GetBoolean())
        {
            var verified = MailhiveWebhook.Verify(payload, header, secret, tolerance, now);
            var expected = JsonDocument.Parse(payload).RootElement;
            Assert.Equal(expected.GetProperty("type").GetString(), verified.Type);
            Assert.Equal(expected.GetProperty("data").GetRawText(), verified.Data.GetRawText());

            // The byte[] overload agrees.
            Assert.Equal(verified.Type, MailhiveWebhook.Verify(System.Text.Encoding.UTF8.GetBytes(payload), header, secret, tolerance, now).Type);
        }
        else
        {
            var error = Assert.Throws<WebhookVerificationException>(() => MailhiveWebhook.Verify(payload, header, secret, tolerance, now));
            Assert.Equal(vector.GetProperty("reason").GetString(), error.Reason);
        }
    }

    [Fact]
    public void ReadsTheEventFields()
    {
        var vector = Vectors.GetProperty("cases")[0];
        var verified = MailhiveWebhook.Verify(
            vector.GetProperty("payload").GetString()!,
            vector.GetProperty("header").GetString(),
            vector.GetProperty("secret").GetString()!,
            now: DateTimeOffset.FromUnixTimeSeconds(vector.GetProperty("now").GetInt64()));
        Assert.Equal("email.delivered", verified.Type);
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero), verified.CreatedAt);
        Assert.False(verified.Test);
        Assert.Equal("ada@example.com", verified.Data.GetProperty("recipient").GetString());
    }

    [Fact]
    public void ANullHeaderIsAHeaderFailure()
    {
        var error = Assert.Throws<WebhookVerificationException>(() => MailhiveWebhook.Verify("{}", null, "whsec_x"));
        Assert.Equal("header", error.Reason);
        Assert.IsAssignableFrom<MailhiveException>(error);
    }
}
