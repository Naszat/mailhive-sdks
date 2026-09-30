using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Mailhive.Internal;
using Xunit;

namespace Mailhive.Tests;

/// <summary>The shared conformance suite (spec/conformance.json): every Mailhive SDK runs these same cases against the same mock server.</summary>
public sealed class ConformanceTests : IClassFixture<MockServerFixture>
{
    private static readonly JsonElement Suite = Spec.Load("conformance.json");

    private static readonly Dictionary<string, Type> Kinds = new()
    {
        ["api"] = typeof(MailhiveApiException),
        ["authentication"] = typeof(AuthenticationException),
        ["billing"] = typeof(BillingException),
        ["permission"] = typeof(PermissionException),
        ["not_found"] = typeof(NotFoundException),
        ["conflict"] = typeof(ConflictException),
        ["validation"] = typeof(ValidationException),
        ["rate_limit"] = typeof(RateLimitException),
    };

    private readonly MockServerFixture _mock;

    public ConformanceTests(MockServerFixture mock) => _mock = mock;

    public static IEnumerable<object[]> Cases() =>
        Suite.GetProperty("cases").EnumerateArray().Select(c => new object[] { c.GetProperty("name").GetString()! });

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Conforms(string name)
    {
        var testCase = Suite.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("name").GetString() == name);
        var want = testCase.GetProperty("expect");
        await _mock.ResetAsync();

        object? result = null;
        MailhiveException? error = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            result = await RunAsync(testCase);
        }
        catch (MailhiveException caught)
        {
            error = caught;
        }
        stopwatch.Stop();
        var requests = await _mock.RequestsAsync();

        Assert.Equal(want.GetProperty("attempts").GetInt32(), requests.Length);

        foreach (var field in new[] { "result", "resultFields" })
        {
            if (!want.TryGetProperty(field, out var expected))
            {
                continue;
            }
            Assert.Null(error);
            var actual = JsonSerializer.SerializeToNode(result, result!.GetType(), Json.Options)!;
            if (expected.ValueKind == JsonValueKind.Array)
            {
                var items = actual.AsArray();
                Assert.Equal(expected.GetArrayLength(), items.Count);
                var index = 0;
                foreach (var item in expected.EnumerateArray())
                {
                    AssertSubset(item, items[index++]!);
                }
            }
            else
            {
                AssertSubset(expected, actual);
            }
        }

        if (want.TryGetProperty("minElapsedMs", out var minElapsed))
        {
            Assert.True(stopwatch.ElapsedMilliseconds >= minElapsed.GetInt32(), $"took {stopwatch.ElapsedMilliseconds}ms");
        }

        if (want.TryGetProperty("sameIdempotencyKey", out var same) && same.GetBoolean())
        {
            var keys = requests.Select(r => Header(r, "idempotency-key")).Distinct().ToList();
            Assert.Single(keys);
            Assert.NotNull(keys[0]);
        }

        if (want.TryGetProperty("error", out var expectedError))
        {
            Assert.NotNull(error);
            var kind = Kinds[expectedError.GetProperty("kind").GetString()!];
            Assert.IsAssignableFrom(kind, error);
            var apiError = Assert.IsAssignableFrom<MailhiveApiException>(error);
            Assert.Equal(expectedError.GetProperty("status").GetInt32(), apiError.StatusCode);
            Assert.Equal(expectedError.GetProperty("code").GetString(), apiError.Code);
            if (expectedError.TryGetProperty("requestId", out var requestId))
            {
                Assert.Equal(requestId.GetString(), apiError.RequestId);
            }
            if (expectedError.TryGetProperty("hasDetails", out var hasDetails) && hasDetails.GetBoolean())
            {
                Assert.NotNull(apiError.Details);
                var details = apiError.Details!.Value;
                Assert.True(
                    details.ValueKind switch
                    {
                        JsonValueKind.Array => details.GetArrayLength() > 0,
                        JsonValueKind.Object => details.EnumerateObject().Any(),
                        _ => true,
                    },
                    "details should not be empty");
            }
        }
        else
        {
            Assert.True(error is null, error?.ToString());
        }

        if (want.TryGetProperty("request", out var request))
        {
            CheckRequest(request, requests[0]);
        }
    }

    private async Task<object> RunAsync(JsonElement testCase)
    {
        using var client = new MailhiveClient(new MailhiveOptions
        {
            ApiKey = testCase.GetProperty("key").GetString(),
            BaseUrl = _mock.Url + "/v1",
            MaxRetries = 2,
        });
        var email = Suite.GetProperty("email").Deserialize<SendEmailRequest>()!;
        var options = testCase.TryGetProperty("idempotencyKey", out var key)
            ? new SendOptions { IdempotencyKey = key.GetString() }
            : null;
        return testCase.GetProperty("call").GetString() switch
        {
            "emails.send" => await client.Emails.SendAsync(email, options),
            "emails.sendBatch" => await client.Emails.SendBatchAsync(new[] { email, email }, options),
            "emails.get" => await client.Emails.GetAsync(testCase.GetProperty("id").GetString()!),
            var call => throw new InvalidOperationException("Unknown call " + call),
        };
    }

    private static void CheckRequest(JsonElement expected, JsonElement first)
    {
        foreach (var check in expected.EnumerateObject())
        {
            var value = check.Value;
            switch (check.Name)
            {
                case "method":
                    Assert.Equal(value.GetString(), first.GetProperty("method").GetString());
                    break;
                case "path":
                    Assert.Equal(value.GetString(), first.GetProperty("path").GetString());
                    break;
                case "authorization":
                    Assert.Equal(value.GetString(), Header(first, "authorization"));
                    break;
                case "contentType":
                    Assert.Equal(value.GetString(), Header(first, "content-type"));
                    break;
                case "userAgentPattern":
                    Assert.Matches(new Regex(value.GetString()!), Header(first, "user-agent") ?? "");
                    break;
                case "idempotencyKeyPattern":
                    Assert.Matches(new Regex(value.GetString()!), Header(first, "idempotency-key") ?? "");
                    break;
                case "idempotencyKey":
                    Assert.Equal(value.GetString(), Header(first, "idempotency-key"));
                    break;
                case "noIdempotencyKey":
                    Assert.Null(Header(first, "idempotency-key"));
                    break;
                case "body":
                    Assert.True(
                        JsonNode.DeepEquals(JsonNode.Parse(value.GetRawText()), JsonNode.Parse(first.GetProperty("body").GetRawText())),
                        $"body {first.GetProperty("body").GetRawText()} != {value.GetRawText()}");
                    break;
                case "bodyEmailCount":
                    Assert.Equal(value.GetInt32(), first.GetProperty("body").GetProperty("emails").GetArrayLength());
                    break;
                default:
                    throw new InvalidOperationException("Unknown request check " + check.Name);
            }
        }
    }

    private static void AssertSubset(JsonElement expected, JsonNode actual)
    {
        foreach (var property in expected.EnumerateObject())
        {
            var actualValue = actual[property.Name];
            Assert.True(
                JsonNode.DeepEquals(JsonNode.Parse(property.Value.GetRawText()), actualValue),
                $"{property.Name}: expected {property.Value.GetRawText()}, got {actualValue?.ToJsonString() ?? "null"}");
        }
    }

    private static string? Header(JsonElement request, string name) =>
        request.GetProperty("headers").TryGetProperty(name, out var value) ? value.GetString() : null;
}
