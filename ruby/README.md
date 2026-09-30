# mailhive

The official Ruby SDK for [Mailhive Send](https://mailhive.africa/docs/send/overview). It supports Ruby 3.0+, has no runtime dependencies, and includes an **ActionMailer** delivery method for Rails.

```sh
bundle add mailhive
# or: gem install mailhive
```

## Send an email

```ruby
require "mailhive"

client = Mailhive::Client.new # reads MAILHIVE_API_KEY

email = client.emails.send(
  from: "Acme <hello@acme.com>",
  to: "ada@example.com",
  subject: "Your receipt",
  html: "<p>Thanks for your order.</p>"
)
puts email[:id]
```

Pass the fields as keyword arguments or as a Hash (symbol or string keys). They match the [API reference](https://mailhive.africa/docs/api/send-email) exactly: `from`, `to`, `cc`, `bcc`, `subject`, `html`, `text`, `template_id`, `variables`, `reply_to`, `headers`, `tags` and `attachments`.

Responses are Hashes with **symbol keys**, parsed from the API's JSON: `email[:id]`, `email[:status]`.

An attachment's `content` is the raw file, which is encoded for you. Use `content_base64` if your data is already base64:

```ruby
client.emails.send(
  from: "hello@acme.com",
  to: "ada@example.com",
  subject: "Your invoice",
  text: "Attached.",
  attachments: [
    { filename: "invoice.pdf", content: File.binread("invoice.pdf"), content_type: "application/pdf" }
  ]
)
```

```ruby
client.emails.send_batch([email1, email2]) # up to 100; all accepted or none. Returns the accepted emails.
client.emails.get(email_id)                # {status: "delivered", …}
```

`emails.send` sends an email: on that object, use `__send__` or `public_send` for Ruby's dynamic dispatch.

## Rails (ActionMailer)

The gem's Railtie registers a `:mailhive` delivery method:

```ruby
# config/environments/production.rb
config.action_mailer.delivery_method = :mailhive
config.action_mailer.mailhive_settings = { api_key: ENV["MAILHIVE_API_KEY"] } # or leave the key in the environment
```

Every mailer then sends through Mailhive: HTML and text parts, attachments, cc, bcc, Reply-To, and your own `X-` headers (plus `In-Reply-To`, `References` and `List-Unsubscribe`). Two headers control Mailhive features and aren't sent on:

```ruby
class ReceiptMailer < ApplicationMailer
  def receipt(order)
    headers["X-Mailhive-Idempotency-Key"] = "order-#{order.id}-receipt" # safe to repeat
    headers["X-Mailhive-Tags"] = { type: "receipt" }.to_json             # tags, returned in webhook events
    mail(to: order.email, subject: "Your receipt")
  end
end
```

After delivery, the message's `X-Mailhive-Email-Id` header holds the email's id. `mailhive_settings` also takes `base_url`, `timeout` and `max_retries`.

Outside Rails, use it with the [mail](https://github.com/mikel/mail) gem:

```ruby
mail.delivery_method Mailhive::ActionMailer::DeliveryMethod, api_key: "mhs_…"
```

## Retries and idempotency

Every send carries an `Idempotency-Key`. Retries reuse it, so **a retry never sends twice**.

- **Retried** (up to 2 times, then configurable): network errors, timeouts, 5xx responses, and `429 rate_limited` after the `Retry-After` delay.
- **Not retried:** a used-up allowance (`monthly_quota_reached`, `daily_cap_reached`) and invalid requests.

To stay safe across restarts, pass your own key:

```ruby
client.emails.send(email, idempotency_key: "order-#{order.id}-receipt")
```

## Errors

```ruby
begin
  client.emails.send(email)
rescue Mailhive::ValidationError => e
  puts e.code, e.message, e.details, e.request_id
rescue Mailhive::RateLimitError => e
  # e.code == "monthly_quota_reached", or wait e.retry_after seconds
end
```

| Error | Status |
|---|---|
| `Mailhive::AuthenticationError` | 401 |
| `Mailhive::BillingError` | 402 |
| `Mailhive::PermissionError` | 403 |
| `Mailhive::NotFoundError` | 404 |
| `Mailhive::ConflictError` | 409 (e.g. `idempotency_conflict`) |
| `Mailhive::ValidationError` | 400, 422 |
| `Mailhive::RateLimitError` | 429 (`retry_after` in seconds) |
| `Mailhive::APIError` | other statuses, and the base class of all of the above |
| `Mailhive::ConnectionError` | no response |

API errors carry `status`, `code`, `message`, `details`, `request_id` and `headers`. Everything the SDK raises is a `Mailhive::Error`.

## Webhooks

Pass the **raw** body, exactly as received. `verify` returns the event as a Hash with symbol keys:

```ruby
class MailhiveWebhooksController < ActionController::API
  def create
    event = Mailhive::Webhook.verify(
      request.raw_post,
      request.headers["Mailhive-Signature"],
      ENV.fetch("MAILHIVE_WEBHOOK_SECRET")
    )
    case event[:type]
    when "email.bounced" then # …
    end
    head :ok
  rescue Mailhive::WebhookVerificationError => e
    head :bad_request # e.reason is "header", "timestamp" or "signature"
  end
end
```

Signatures older than 300 seconds are refused (`tolerance:` changes that). Several `v1=` values are accepted, so secrets can be rotated.

## Options

```ruby
Mailhive::Client.new(
  api_key: "mhs_…",                                 # default: MAILHIVE_API_KEY
  base_url: "https://api-beta.mailhive.africa/v1",  # default: MAILHIVE_BASE_URL, then production
  timeout: 30,                                      # seconds
  max_retries: 2
)
```

The key is never included in `inspect` or logs.

**Test keys** (`mhs_test_…`) work unchanged: delivery is simulated and nothing is billed. [More about test keys](https://mailhive.africa/docs/send/api-keys#test-keys).

## Development

```sh
bundle config set --local path vendor/bundle
bundle install
bundle exec rake test # needs node on PATH for the shared mock server
```

This gem lives in the [mailhive-sdks](https://github.com/Naszat/mailhive-sdks) monorepo. Please open issues and pull requests there.

## Releasing (maintainers)

1. Bump `Mailhive::VERSION` and the changelog, then merge to `main`.
2. Push the tag `ruby-vX.Y.Z`. The `release-ruby` workflow publishes the gem to RubyGems through trusted publishing.
