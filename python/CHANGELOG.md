# Changelog

## 0.1.0 (unreleased)

- `Mailhive` and `AsyncMailhive`, with `emails.send`, `emails.send_batch` and `emails.get`.
- Idempotency keys reused across retries, and `Retry-After` honoured.
- Typed errors that carry `request_id`.
- `verify_webhook`, with secret rotation.
- The Django email backend `mailhive.django.EmailBackend`.
