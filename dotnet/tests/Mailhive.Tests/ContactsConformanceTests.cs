using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mailhive.Internal;
using Xunit;

namespace Mailhive.Tests;

/// <summary>spec/conformance.json "contactCases": audiences and contacts, run against the shared mock server.</summary>
public sealed class ContactsConformanceTests : IClassFixture<MockServerFixture>
{
    private static readonly JsonElement Suite = Spec.Load("conformance.json").GetProperty("contactCases");

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

    public ContactsConformanceTests(MockServerFixture mock) => _mock = mock;

    public static IEnumerable<object[]> Cases() =>
        Suite.GetProperty("cases").EnumerateArray().Select(c => new object[] { c.GetProperty("name").GetString()! });

    /// <summary>A string naming a fixture stands for it; anything else is itself.</summary>
    private static JsonElement Fixture(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && Suite.TryGetProperty(value.GetString()!, out var named) ? named : value;

    private static ContactInput Contact(JsonElement value, JsonElement? doubleOptIn = null)
    {
        var contact = Fixture(value).Deserialize<ContactInput>()!;
        if (doubleOptIn is { } d)
        {
            contact.DoubleOptIn = Fixture(d).Deserialize<DoubleOptIn>()!;
        }
        return contact;
    }

    private static JsonElement? Arg(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) ? value : null;

    private async Task<object?> RunAsync(JsonElement testCase)
    {
        using var client = new MailhiveClient(new MailhiveOptions
        {
            ApiKey = testCase.GetProperty("key").GetString(),
            BaseUrl = _mock.Url + "/v1",
            MaxRetries = 2,
        });
        var audience = Suite.GetProperty("audienceId").GetString()!;
        testCase.TryGetProperty("args", out var a);
        switch (testCase.GetProperty("call").GetString())
        {
            case "audiences.list":
                return await client.Audiences.ListAsync();
            case "audiences.create":
                return await client.Audiences.CreateAsync(Arg(a, "name")!.Value.GetString()!);
            case "audiences.rename":
                return await client.Audiences.RenameAsync(Arg(a, "id")!.Value.GetString()!, Arg(a, "name")!.Value.GetString()!);
            case "contacts.add":
                return await client.Contacts.AddAsync(audience, Contact(Arg(a, "contact")!.Value));
            case "contacts.upsert":
                return await client.Contacts.UpsertAsync(audience, Contact(Arg(a, "contact")!.Value, Arg(a, "doubleOptIn")));
            case "contacts.upsertBatch":
                return await client.Contacts.UpsertBatchAsync(audience, Arg(a, "contacts")!.Value.EnumerateArray().Select(c => Contact(c)).ToList());
            case "contacts.list":
                return await client.Contacts.ListAsync(audience, new ListContactsOptions
                {
                    Email = Arg(a, "email")?.GetString(),
                    Status = Arg(a, "status")?.GetString(),
                    Page = Arg(a, "page")?.GetInt32(),
                    PageSize = Arg(a, "pageSize")?.GetInt32(),
                });
            case "contacts.get":
                return await client.Contacts.GetAsync(Arg(a, "id")!.Value.GetString()!);
            case "contacts.update":
                return await client.Contacts.UpdateAsync(
                    Arg(a, "id")!.Value.GetString()!, Arg(a, "changes")!.Value.Deserialize<ContactUpdate>()!);
            case "contacts.unsubscribe":
                return await client.Contacts.UnsubscribeAsync(Arg(a, "id")!.Value.GetString()!);
            case "contacts.delete":
                await client.Contacts.DeleteAsync(Arg(a, "id")!.Value.GetString()!);
                return null;
            case var call:
                throw new InvalidOperationException("Unknown call " + call);
        }
    }

    private static JsonNode? At(JsonNode? node, string path)
    {
        foreach (var part in path.Split('.'))
        {
            node = node switch
            {
                JsonArray array => array[int.Parse(part)],
                JsonObject obj => obj[part],
                _ => null,
            };
        }
        return node;
    }

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

        var actual = result is null ? null : JsonSerializer.SerializeToNode(result, result.GetType(), Json.Options);
        if (want.TryGetProperty("resultFields", out var fields))
        {
            Assert.Null(error);
            foreach (var field in fields.EnumerateObject())
            {
                var got = At(actual, field.Name);
                Assert.True(
                    JsonNode.DeepEquals(JsonNode.Parse(field.Value.GetRawText()), got),
                    $"{field.Name}: expected {field.Value.GetRawText()}, got {got?.ToJsonString() ?? "null"}");
            }
        }
        if (want.TryGetProperty("resultCount", out var count))
        {
            Assert.Equal(count.GetInt32(), actual!.AsArray().Count);
        }
        if (want.TryGetProperty("resultIsEmpty", out var empty) && empty.GetBoolean())
        {
            Assert.Null(result);
            Assert.Null(error);
        }
        if (want.TryGetProperty("minElapsedMs", out var minElapsed))
        {
            Assert.True(stopwatch.ElapsedMilliseconds >= minElapsed.GetInt32(), $"took {stopwatch.ElapsedMilliseconds}ms");
        }

        if (want.TryGetProperty("error", out var expectedError))
        {
            Assert.NotNull(error);
            Assert.IsAssignableFrom(Kinds[expectedError.GetProperty("kind").GetString()!], error);
            var apiError = Assert.IsAssignableFrom<MailhiveApiException>(error);
            Assert.Equal(expectedError.GetProperty("status").GetInt32(), apiError.StatusCode);
            Assert.Equal(expectedError.GetProperty("code").GetString(), apiError.Code);
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
                case "noIdempotencyKey":
                    Assert.Null(Header(first, "idempotency-key"));
                    break;
                case "query":
                    Assert.True(
                        JsonNode.DeepEquals(JsonNode.Parse(value.GetRawText()), JsonNode.Parse(first.GetProperty("query").GetRawText())),
                        $"query {first.GetProperty("query").GetRawText()} != {value.GetRawText()}");
                    break;
                case "body":
                    Assert.True(
                        JsonNode.DeepEquals(JsonNode.Parse(Fixture(value).GetRawText()), JsonNode.Parse(first.GetProperty("body").GetRawText())),
                        $"body {first.GetProperty("body").GetRawText()} != {value.GetRawText()}");
                    break;
                case "bodyIncludes":
                    foreach (var property in value.EnumerateObject())
                    {
                        Assert.True(
                            JsonNode.DeepEquals(
                                JsonNode.Parse(property.Value.GetRawText()),
                                JsonNode.Parse(first.GetProperty("body").GetProperty(property.Name).GetRawText())),
                            $"body.{property.Name}");
                    }
                    break;
                case "bodyContactCount":
                    Assert.Equal(value.GetInt32(), first.GetProperty("body").GetProperty("contacts").GetArrayLength());
                    break;
                default:
                    throw new InvalidOperationException("Unknown request check " + check.Name);
            }
        }
    }

    private static string? Header(JsonElement request, string name) =>
        request.GetProperty("headers").TryGetProperty(name, out var value) ? value.GetString() : null;
}
