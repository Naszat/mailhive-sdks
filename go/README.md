# mailhive (Go)

The official Go SDK for [Mailhive Send](https://mailhive.africa/docs/send/overview). It supports Go 1.21+ and uses only the standard library.

```sh
go get github.com/Naszat/mailhive-sdks/go
```

```go
import mailhive "github.com/Naszat/mailhive-sdks/go"
```

## Send an email

```go
client, err := mailhive.New() // reads MAILHIVE_API_KEY
if err != nil {
	log.Fatal(err)
}

email, err := client.Emails.Send(ctx, &mailhive.SendEmailParams{
	From:    "Acme <hello@acme.com>",
	To:      mailhive.Recipients{"ada@example.com"},
	Subject: "Your receipt",
	HTML:    "<p>Thanks for your order.</p>",
})
if err != nil {
	log.Fatal(err)
}
fmt.Println(email.ID, email.Status)
```

The JSON field names match the [API reference](https://mailhive.africa/docs/api/send-email) exactly: `Cc`, `Bcc`, `ReplyTo`, `Headers`, `Tags`, `TemplateID`, `Variables` and `Attachments` are sent as `cc`, `bcc`, `reply_to`, `headers`, `tags`, `template_id`, `variables` and `attachments`. Empty fields are left out.

`Attachment.Content` is the file's raw bytes; they are base64-encoded for you:

```go
pdf, _ := os.ReadFile("receipt.pdf")
params.Attachments = []mailhive.Attachment{{Filename: "receipt.pdf", Content: pdf, ContentType: "application/pdf"}}
```

### Batches and lookups

```go
accepted, err := client.Emails.SendBatch(ctx, []mailhive.SendEmailParams{email1, email2}) // up to 100; all accepted or none
email, err := client.Emails.Get(ctx, id)                                                  // Status: "delivered", "bounced", …
```

A `Client` is safe for concurrent use: create one and share it.

## Retries and idempotency

Every send carries an `Idempotency-Key`. Retries reuse it, so **a retry never sends twice**.

- **Retried** (up to 2 times by default): network errors, timeouts, 5xx responses, and `429 rate_limited`. The SDK waits for `Retry-After`, or backs off exponentially with jitter (about 0.5s, 1s, 2s … up to 8s). If `Retry-After` asks for more than 60 seconds, it returns the error instead.
- **Not retried:** a used-up allowance (`monthly_quota_reached`, `daily_cap_reached`) and invalid requests.

Waits respect the context: cancel it and the call returns at once.

To stay safe across restarts, pass your own key:

```go
client.Emails.Send(ctx, params, mailhive.WithIdempotencyKey(fmt.Sprintf("order-%d-receipt", order.ID)))
```

## Errors

API errors are `*mailhive.APIError` values carrying `Status`, `Code`, `Message`, `Details` (raw JSON) and `RequestID`. Well-known statuses come wrapped in a specific type; use `errors.As`:

```go
_, err := client.Emails.Send(ctx, params)

var validation *mailhive.ValidationError
var rateLimit *mailhive.RateLimitError
var apiErr *mailhive.APIError
var connErr *mailhive.ConnectionError
switch {
case errors.As(err, &validation):
	log.Printf("invalid: %s %s", validation.Message, validation.Details)
case errors.As(err, &rateLimit) && rateLimit.Code == "monthly_quota_reached":
	// waiting won't help: upgrade or wait for the next period
case errors.As(err, &apiErr):
	log.Printf("%s (%d %s), request %s", apiErr.Message, apiErr.Status, apiErr.Code, apiErr.RequestID)
case errors.As(err, &connErr):
	log.Printf("no answer: %s", connErr.Message)
}
```

Check `Code`, not `Message`: codes are stable.

| Type | Status | `Kind()` |
|---|---|---|
| `*AuthenticationError` | 401 | `authentication` |
| `*BillingError` | 402 | `billing` |
| `*PermissionError` | 403 | `permission` |
| `*NotFoundError` | 404 | `not_found` |
| `*ConflictError` | 409 (e.g. `idempotency_conflict`) | `conflict` |
| `*ValidationError` | 400, 422 | `validation` |
| `*RateLimitError` | 429 (`RetryAfter()`) | `rate_limit` |
| `*APIError` | any other status; also found inside every type above | `api` |
| `*ConnectionError` | no response (`Timeout` says whether it timed out) | `connection` |

`New` returns `ErrMissingAPIKey` without a key and `ErrPublishableKey` for a form's publishable key (`mhp_…`): the server SDK needs a secret key (`mhs_…`).

## Webhooks

Pass the **raw** body, exactly as received. Decoding and re-encoding it changes the bytes, and the signature won't match.

```go
http.HandleFunc("/webhooks/mailhive", func(w http.ResponseWriter, r *http.Request) {
	body, err := io.ReadAll(http.MaxBytesReader(w, r.Body, 1<<20))
	if err != nil {
		http.Error(w, "bad body", http.StatusBadRequest)
		return
	}
	event, err := mailhive.VerifyWebhook(body, r.Header.Get("Mailhive-Signature"), os.Getenv("MAILHIVE_WEBHOOK_SECRET"))
	if err != nil {
		http.Error(w, "invalid signature", http.StatusBadRequest)
		return
	}
	switch event.Type {
	case "email.bounced":
		log.Printf("bounced: %s", event.Data.Recipient)
	}
	w.WriteHeader(http.StatusNoContent)
})
```

`VerifyWebhook` checks the `Mailhive-Signature` header (`t=<unix>,v1=<hex>`, HMAC-SHA256 of `"<t>.<raw body>"`) in constant time, with a 300-second tolerance. It accepts several `v1` values, so secrets can be rotated. On failure it returns a `*WebhookVerificationError` whose `Reason` is `header`, `timestamp` or `signature`. `mailhive.WithTolerance(d)` changes the tolerance and `mailhive.WithNow(t)` fixes the clock for tests. `event.Raw` holds the whole event for fields the struct doesn't cover.

## Options

```go
client, err := mailhive.New(
	mailhive.WithAPIKey("mhs_…"),                                 // default: MAILHIVE_API_KEY
	mailhive.WithBaseURL("https://api-beta.mailhive.africa/v1"), // default: MAILHIVE_BASE_URL, then production
	mailhive.WithTimeout(30*time.Second),                        // per attempt
	mailhive.WithMaxRetries(2),
	mailhive.WithHTTPClient(&http.Client{Transport: myTransport}),
)
```

The key is never printed: `fmt.Println(client)` shows only the base URL.

**Test keys** (`mhs_test_…`) work unchanged: delivery is simulated, nothing is billed, and `AcceptedEmail.Test` is `true`. [More about test keys](https://mailhive.africa/docs/send/api-keys#test-keys).

## For maintainers

```sh
cd go
go vet ./... && test -z "$(gofmt -l .)" && go test ./...
```

The tests start the shared mock server (`node ../mock-server/server.mjs`), so they need `node` on `PATH`; without it the conformance suite is skipped.

Go modules are published by tags, not by a workflow. Update `Version` in `version.go` and `CHANGELOG.md`, merge to `main`, then tag the commit with `go/vX.Y.Z` (for example `go/v0.1.0`) and push the tag. The Go proxy picks it up on the first `go get`.
