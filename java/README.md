# africa.mailhive:mailhive-java

The official Java SDK for [Mailhive Send](https://mailhive.africa/docs/send/overview). It needs Java 11+ and depends only on Jackson (`jackson-databind`). It works from Kotlin too.

Maven:

```xml
<dependency>
  <groupId>africa.mailhive</groupId>
  <artifactId>mailhive-java</artifactId>
  <version>0.1.0</version>
</dependency>
```

Gradle:

```kotlin
implementation("africa.mailhive:mailhive-java:0.1.0")
```

## Send an email

```java
import africa.mailhive.AcceptedEmail;
import africa.mailhive.Mailhive;
import africa.mailhive.SendEmailParams;

Mailhive client = Mailhive.fromEnv(); // reads MAILHIVE_API_KEY

AcceptedEmail email = client.emails().send(SendEmailParams.builder()
    .from("Acme <hello@acme.com>")
    .to("ada@example.com")
    .subject("Your receipt")
    .html("<p>Thanks for your order.</p>")
    .build());
System.out.println(email.id());
```

The client is thread-safe: create one and share it.

The builder's fields match the [API reference](https://mailhive.africa/docs/api/send-email): `cc`, `bcc`, `replyTo` (`reply_to`), `headers`, `tags`, `templateId` (`template_id`), `variables` and `attachments`. `to`, `cc`, `bcc` and `replyTo` take one address or several.

Attachments take the raw file, which is base64-encoded for you, or text that's already base64:

```java
import africa.mailhive.Attachment;
import java.nio.file.Files;
import java.nio.file.Path;

SendEmailParams.builder()
    // …
    .attachment(Attachment.of("invoice.pdf", Files.readAllBytes(Path.of("invoice.pdf")), "application/pdf"))
    .attachment(Attachment.ofBase64("note.txt", "aGVsbG8="))
    .build();
```

Templates:

```java
SendEmailParams.builder()
    .from("hello@acme.com")
    .to("ada@example.com")
    .templateId("tmpl_receipt")
    .variable("name", "Ada")
    .variable("total", 42)
    .build();
```

Batches and lookups:

```java
List<AcceptedEmail> accepted = client.emails().sendBatch(List.of(email1, email2)); // up to 100; all accepted or none
Email sent = client.emails().get(id);                                              // sent.status(): "delivered", "bounced", …
```

## Retries and idempotency

Every send carries an `Idempotency-Key`. Retries reuse it, so **a retry never sends twice**.

- **Retried** (up to 2 times, then configurable): network errors, timeouts, 5xx responses, and `429 rate_limited` after the `Retry-After` delay (or an exponential backoff with jitter).
- **Not retried:** a used-up allowance (`monthly_quota_reached`, `daily_cap_reached`) and invalid requests.

To stay safe across restarts, pass your own key:

```java
import africa.mailhive.RequestOptions;

client.emails().send(email, RequestOptions.idempotencyKey("order-" + order.getId() + "-receipt"));
```

## Errors

Every exception is unchecked and extends `MailhiveException`.

```java
import africa.mailhive.RateLimitException;
import africa.mailhive.ValidationException;

try {
    client.emails().send(email);
} catch (ValidationException e) {
    System.err.println(e.code() + " " + e.getMessage() + " " + e.details() + " " + e.requestId());
} catch (RateLimitException e) {
    if (e.code().equals("monthly_quota_reached")) {
        // …
    }
    e.retryAfter(); // Optional<Double>, in seconds
}
```

| Exception | Status |
|---|---|
| `AuthenticationException` | 401 |
| `BillingException` | 402 |
| `PermissionException` | 403 |
| `NotFoundException` | 404 |
| `ConflictException` | 409 (e.g. `idempotency_conflict`) |
| `ValidationException` | 400, 422 (`details()` lists every problem) |
| `RateLimitException` | 429 |
| `ApiException` | other statuses, and the base class of all of the above |
| `ConnectionException` | no response |

An `ApiException` carries `status()`, `code()`, `getMessage()`, `details()` (a Jackson `JsonNode`), `requestId()` and `headers()`. Check `code()`, not the message, and quote `requestId()` to support.

## Webhooks

`Webhooks.verify` checks the `Mailhive-Signature` header and returns the event. Pass the **raw** request body: parsing and re-serializing JSON changes the bytes, so the signature can't match. Several `v1=` values are accepted, so you can rotate secrets. With Spring Boot:

```java
import africa.mailhive.WebhookEvent;
import africa.mailhive.WebhookVerificationException;
import africa.mailhive.Webhooks;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

@RestController
class MailhiveWebhookController {
    private final String secret = System.getenv("MAILHIVE_WEBHOOK_SECRET");

    @PostMapping("/webhooks/mailhive")
    ResponseEntity<Void> receive(@RequestBody String rawBody,
                                 @RequestHeader("Mailhive-Signature") String signature) {
        WebhookEvent event;
        try {
            event = Webhooks.verify(rawBody, signature, secret);
        } catch (WebhookVerificationException e) {
            return ResponseEntity.badRequest().build(); // e.reason(): header, timestamp or signature
        }
        if (event.type().equals("email.bounced")) {
            String emailId = event.data().get("email_id").asText();
            // …
        }
        return ResponseEntity.ok().build();
    }
}
```

Signatures older than 300 seconds are refused. `Webhooks.verify(payload, header, secret, tolerance, now)` takes your own tolerance and clock, and a `byte[]` body works too.

## Options

```java
Mailhive client = Mailhive.builder()
    .apiKey("mhs_…")                                   // default: MAILHIVE_API_KEY
    .baseUrl("https://api-beta.mailhive.africa/v1")    // default: MAILHIVE_BASE_URL, then production
    .timeout(Duration.ofSeconds(30))                   // per attempt
    .maxRetries(2)
    .httpClient(HttpClient.newBuilder().proxy(…).build()) // optional: your own java.net.http.HttpClient
    .build();
```

Secret keys start with `mhs_`. A form's publishable key (`mhp_…`) is refused. The key never appears in `toString()`.

**Test keys** (`mhs_test_…`) work unchanged: delivery is simulated, nothing is billed, and `AcceptedEmail.test()` is `true`.

This package is published from the [mailhive-sdks](https://github.com/Naszat/mailhive-sdks) monorepo. Please open issues and pull requests there.

## Development

```sh
./mvnw verify   # needs node on PATH: the conformance tests run ../mock-server
```

## Releasing (maintainers)

1. Merge to `main` in mailhive-sdks.
2. Push the tag `java-vX.Y.Z` there.
3. The `release-java` workflow sets the version from the tag, signs the artifacts and publishes them to Maven Central. The workflow file lists the one-time setup.
