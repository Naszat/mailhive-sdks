# mailhive

The official Python SDK for [Mailhive Send](https://mailhive.africa/docs/send/overview). It supports Python 3.9+, with sync and async clients and a Django email backend.

```sh
pip install mailhive
```

## Send an email

```python
from mailhive import Mailhive

client = Mailhive()  # reads MAILHIVE_API_KEY

email = client.emails.send({
    "from": "Acme <hello@acme.com>",
    "to": "ada@example.com",
    "subject": "Your receipt",
    "html": "<p>Thanks for your order.</p>",
})
print(email["id"])
```

The keys match the [API reference](https://mailhive.africa/docs/api/send-email) exactly: `cc`, `bcc`, `reply_to`, `headers`, `tags`, `template_id`, `variables` and `attachments`. You can also pass keyword arguments, with `from_` standing in for `from`:

```python
client.emails.send(from_="hello@acme.com", to="ada@example.com", subject="Hi", text="Hello")
```

Attachment `content` can be `bytes`, which are encoded for you, or base64 text.

```python
client.emails.send_batch([email1, email2])   # up to 100; all accepted or none
client.emails.get(email_id)                  # status: "delivered", "bounced", …
```

### Async

```python
from mailhive import AsyncMailhive

async with AsyncMailhive() as client:
    await client.emails.send({...})
```

## Retries and idempotency

Every send carries an `Idempotency-Key`. Retries reuse it, so **a retry never sends twice**.

- **Retried** (up to 2 times, then configurable): network errors, timeouts, 5xx responses, and `429 rate_limited` after the `Retry-After` delay.
- **Not retried:** a used-up allowance (`monthly_quota_reached`, `daily_cap_reached`) and invalid requests.

To stay safe across restarts, pass your own key:

```python
client.emails.send(email, idempotency_key=f"order-{order.id}-receipt")
```

## Errors

```python
from mailhive import RateLimitError, ValidationError

try:
    client.emails.send(email)
except ValidationError as error:
    print(error.code, error.message, error.details, error.request_id)
except RateLimitError as error:
    if error.code == "monthly_quota_reached":
        ...
```

| Exception | Status |
|---|---|
| `AuthenticationError` | 401 |
| `BillingError` | 402 |
| `PermissionDeniedError` | 403 |
| `NotFoundError` | 404 |
| `ConflictError` | 409 (e.g. `idempotency_conflict`) |
| `ValidationError` | 422 |
| `RateLimitError` | 429 (`retry_after` in seconds) |
| `APIError` | other statuses, and the base class of all of the above |
| `APIConnectionError` | no response |

All of them subclass `MailhiveError`.

## Webhooks

Pass the **raw** body, exactly as received:

```python
from mailhive import WebhookVerificationError, verify_webhook

# Django
def mailhive_webhook(request):
    try:
        event = verify_webhook(request.body, request.headers.get("Mailhive-Signature"), settings.MAILHIVE_WEBHOOK_SECRET)
    except WebhookVerificationError:
        return HttpResponse(status=400)
    ...

# Flask: verify_webhook(request.get_data(), request.headers.get("Mailhive-Signature"), secret)
# FastAPI: verify_webhook(await request.body(), request.headers.get("mailhive-signature"), secret)
```

## Django

```python
# settings.py, Django 6.1 and later
MAILERS = {
    "default": {
        "BACKEND": "mailhive.django.EmailBackend",
        "OPTIONS": {"api_key": os.environ["MAILHIVE_API_KEY"]},
    },
}

# settings.py, earlier versions
EMAIL_BACKEND = "mailhive.django.EmailBackend"
MAILHIVE_API_KEY = os.environ["MAILHIVE_API_KEY"]
```

`send_mail`, `EmailMessage` and `EmailMultiAlternatives` all send through Mailhive, including HTML alternatives, attachments, cc, bcc, Reply-To and extra headers. You can also set `message.mailhive_tags` or `message.mailhive_idempotency_key` on a message.

## Options

```python
Mailhive(
    api_key="mhs_…",                                 # default: MAILHIVE_API_KEY
    base_url="https://api-beta.mailhive.africa/v1",  # default: MAILHIVE_BASE_URL, then production
    timeout=30.0,
    max_retries=2,
)
```

**Test keys** (`mhs_test_…`) work unchanged: delivery is simulated and nothing is billed. [More about test keys](https://mailhive.africa/docs/send/api-keys#test-keys).
