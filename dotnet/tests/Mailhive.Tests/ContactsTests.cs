using System.Net;
using System.Text.Json;
using Mailhive.Internal;
using Xunit;

namespace Mailhive.Tests;

public sealed class ContactsTests
{
    private static readonly object Contact = new { id = "c1", email = "ada@example.com", status = "subscribed" };

    private static (MailhiveClient Client, FakeHandler Handler) ClientWith(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeHandler(respond);
        return (new MailhiveClient(new MailhiveOptions { ApiKey = "mhs_x" }, new HttpClient(handler), _ => null), handler);
    }

    [Fact]
    public void AnInputLeavesOutWhatIsNotSetAndSendsEmptyStringsToClear()
    {
        Assert.Equal("{\"email\":\"ada@example.com\"}", JsonSerializer.Serialize(new ContactInput { Email = "ada@example.com" }, Json.Options));
        var input = new ContactInput
        {
            Email = "ada@example.com",
            FirstName = "",
            Fields = new Dictionary<string, string> { ["plan"] = "pro" },
            DoubleOptIn = new DoubleOptIn { From = "Acme <news@acme.com>", Subject = "Confirm" },
        };
        Assert.Equal(
            "{\"email\":\"ada@example.com\",\"first_name\":\"\",\"fields\":{\"plan\":\"pro\"},\"double_opt_in\":{\"from\":\"Acme <news@acme.com>\",\"subject\":\"Confirm\"}}",
            JsonSerializer.Serialize(input, Json.Options));
    }

    [Fact]
    public void AnUpdateSendsEmptyFieldsAsAnObjectAndUnsubscribeAsTrue()
    {
        Assert.Equal("{}", JsonSerializer.Serialize(new ContactUpdate(), Json.Options));
        Assert.Equal("{\"fields\":{}}", JsonSerializer.Serialize(new ContactUpdate { Fields = new Dictionary<string, string>() }, Json.Options));
        Assert.Equal("{\"unsubscribe\":true}", JsonSerializer.Serialize(new ContactUpdate { Unsubscribe = true }, Json.Options));
    }

    [Fact]
    public void ListFiltersArePercentEncodedAndOptional()
    {
        Assert.Equal("", ContactsResource.QueryString(null));
        Assert.Equal("", ContactsResource.QueryString(new ListContactsOptions()));
        Assert.Equal(
            "?email=a%2Bb%40example.com&page=2&page_size=50",
            ContactsResource.QueryString(new ListContactsOptions { Email = "a+b@example.com", Page = 2, PageSize = 50 }));
    }

    [Fact]
    public async Task UpsertIsAPutWithNoIdempotencyKey()
    {
        var (client, handler) = ClientWith(_ => FakeHandler.Json(200, Contact));
        var saved = await client.Contacts.UpsertAsync("aud 1", new ContactInput { Email = "ada@example.com" });
        Assert.Equal("c1", saved.Id);
        var request = handler.Requests[0].Request;
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("https://api.mailhive.africa/v1/send/audiences/aud%201/contacts", request.RequestUri!.AbsoluteUri);
        Assert.False(request.Headers.Contains("Idempotency-Key"));
    }

    [Fact]
    public async Task AddAndBatchArePostsWithAnIdempotencyKey()
    {
        var (client, handler) = ClientWith(_ => FakeHandler.Json(201, Contact));
        await client.Contacts.AddAsync("aud_1", new ContactInput { Email = "ada@example.com" }, new SendOptions { IdempotencyKey = "signup-1" });
        Assert.Equal("signup-1", handler.Requests[0].Request.Headers.GetValues("Idempotency-Key").Single());
    }

    [Fact]
    public async Task ADeleteWithNoBodySucceedsAndAPatchIsAPatch()
    {
        var (client, handler) = ClientWith(request =>
            request.Method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.NoContent) : FakeHandler.Json(200, Contact));
        await client.Contacts.DeleteAsync("c1");
        await client.Contacts.UnsubscribeAsync("c1");
        Assert.Equal(HttpMethod.Delete, handler.Requests[0].Request.Method);
        Assert.Equal("PATCH", handler.Requests[1].Request.Method.Method);
        Assert.Equal("{\"unsubscribe\":true}", handler.Requests[1].Body);
    }

    [Fact]
    public async Task IdsAreRequired()
    {
        var (client, _) = ClientWith(_ => FakeHandler.Json(200, Contact));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Contacts.GetAsync(""));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.Contacts.UpsertAsync("aud_1", null!));
    }
}
