# Mailhive

The official .NET SDK for [Mailhive Send](https://mailhive.africa/docs/send/overview). It targets .NET 8 and .NET Standard 2.0, so it also runs on .NET Framework 4.6.2 and later.

```sh
dotnet add package Mailhive
```

## Send an email

```csharp
using Mailhive;

using var client = new MailhiveClient(); // reads MAILHIVE_API_KEY

var email = await client.Emails.SendAsync(new SendEmailRequest
{
    From = "Acme <hello@acme.com>",
    To = "ada@example.com",
    Subject = "Your receipt",
    Html = "<p>Thanks for your order.</p>",
});
Console.WriteLine(email.Id);
```

The JSON names match the [API reference](https://mailhive.africa/docs/api/send-email) exactly. `SendEmailRequest` also has `Cc`, `Bcc`, `ReplyTo`, `Headers`, `Tags`, `TemplateId`, `Variables` and `Attachments`. Properties you leave `null` aren't sent.

Recipients take one address or several:

```csharp
To = "ada@example.com",
Cc = new[] { "grace@example.com", "Alan <alan@example.com>" },
```

Attachments take bytes, which are base64-encoded for you, or text that's already base64:

```csharp
Attachments = new List<Attachment>
{
    new Attachment("receipt.pdf", pdfBytes, "application/pdf"),
    Attachment.FromBase64("logo.png", logoBase64, "image/png"),
    Attachment.FromFile("terms.txt"),
},
```

### Templates

```csharp
await client.Emails.SendAsync(new SendEmailRequest
{
    From = "hello@acme.com",
    To = "ada@example.com",
    TemplateId = "tpl_…",
    Variables = new Dictionary<string, object?> { ["name"] = "Ada", ["total"] = 42.5 },
});
```

### Batch and look-up

```csharp
IReadOnlyList<AcceptedEmail> accepted = await client.Emails.SendBatchAsync(new[] { email1, email2 }); // up to 100; all accepted or none
Email sent = await client.Emails.GetAsync(accepted[0].Id); // sent.Status: "delivered", "bounced", …
```

Every method takes a `CancellationToken`.

## Retries and idempotency

Every send carries an `Idempotency-Key`. Retries reuse it, so **a retry never sends twice**.

- **Retried** (up to 2 times by default): network errors, timeouts, 5xx responses, and `429 rate_limited` after the `Retry-After` delay. Otherwise the SDK backs off exponentially with jitter (about 0.5s, 1s, 2s, up to 8s).
- **Not retried:** a used-up allowance (`monthly_quota_reached`, `daily_cap_reached`), invalid requests, and a `Retry-After` longer than 60 seconds.

To stay safe across restarts, pass your own key:

```csharp
await client.Emails.SendAsync(email, new SendOptions { IdempotencyKey = $"order-{order.Id}-receipt" });
```

## Errors

```csharp
try
{
    await client.Emails.SendAsync(email);
}
catch (ValidationException error)
{
    Console.WriteLine($"{error.Code}: {error.Message} {error.Details} ({error.RequestId})");
}
catch (RateLimitException error) when (error.Code == "monthly_quota_reached")
{
    // Upgrade, or wait for next month.
}
```

| Exception | Status |
|---|---|
| `AuthenticationException` | 401 |
| `BillingException` | 402 |
| `PermissionException` | 403 |
| `NotFoundException` | 404 |
| `ConflictException` | 409 (e.g. `idempotency_conflict`) |
| `ValidationException` | 400, 422 |
| `RateLimitException` | 429 (`RetryAfter`) |
| `MailhiveApiException` | other statuses, and the base class of all of the above |
| `MailhiveConnectionException` | no response |

API errors carry `StatusCode`, `Code` (stable: check this, not the message), `Message`, `Details` (a `JsonElement?`), `RequestId` (quote it to support) and `Headers`. Every exception the SDK throws is a `MailhiveException`.

## Webhooks

Pass the **raw** body, exactly as received. Parsing and re-serializing it changes the bytes, so the signature can't match.

```csharp
// ASP.NET Core minimal API
app.MapPost("/webhooks/mailhive", async (HttpRequest request, IConfiguration config) =>
{
    var payload = await new StreamReader(request.Body).ReadToEndAsync();
    try
    {
        var evt = MailhiveWebhook.Verify(
            payload,
            request.Headers[MailhiveWebhook.SignatureHeader],
            config["Mailhive:WebhookSecret"]!);

        if (evt.Type == "email.bounced")
        {
            var address = evt.Data.GetProperty("recipient").GetString();
            // …
        }
        return Results.Ok();
    }
    catch (WebhookVerificationException)
    {
        return Results.BadRequest();
    }
});
```

`Verify` checks the HMAC-SHA256 signature in constant time, accepts several `v1=` values (so secrets can be rotated), and refuses timestamps more than 300 seconds from now (pass `tolerance` to change it). The event has `Type`, `CreatedAt`, `Test`, `Data` and `Raw` (both `JsonElement`). A failure's `Reason` is `header`, `timestamp` or `signature`.

## Dependency injection

`MailhiveClient` is thread-safe: register one and reuse it.

```csharp
builder.Services.AddSingleton(new MailhiveClient(new MailhiveOptions
{
    ApiKey = builder.Configuration["Mailhive:ApiKey"],
}));

// or bind every option from configuration ("Mailhive": { "ApiKey": "…", "Timeout": "00:00:10" })
builder.Services.AddSingleton(_ =>
    new MailhiveClient(builder.Configuration.GetSection("Mailhive").Get<MailhiveOptions>()));
```

Then take `MailhiveClient` in your constructors or endpoint parameters. To send through your own `HttpClient` (for example one with a proxy), pass it as the second argument. The client doesn't dispose an `HttpClient` you pass in.

## Options

```csharp
new MailhiveClient(new MailhiveOptions
{
    ApiKey = "mhs_…",                                 // default: MAILHIVE_API_KEY
    BaseUrl = "https://api-beta.mailhive.africa/v1",  // default: MAILHIVE_BASE_URL, then production
    Timeout = TimeSpan.FromSeconds(30),               // per attempt
    MaxRetries = 2,
});
```

The server SDK needs a **secret** key (`mhs_…`). It refuses a form's publishable key (`mhp_…`), and never includes the key in `ToString()`.

**Test keys** (`mhs_test_…`) work unchanged: delivery is simulated and nothing is billed, and `AcceptedEmail.Test` is `true`. [More about test keys](https://mailhive.africa/docs/send/api-keys#test-keys).

## Development

```sh
cd dotnet
dotnet test   # needs node on PATH: the conformance suite runs against mock-server/server.mjs
dotnet pack -c Release
```

Releases are tagged `dotnet-v<version>` from `main`; the tag must match `<Version>` in `src/Mailhive/Mailhive.csproj`.
