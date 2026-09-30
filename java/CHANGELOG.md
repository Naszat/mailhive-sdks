# Changelog

## 0.1.0 (unreleased)

- `Mailhive`, with `emails().send`, `emails().sendBatch` and `emails().get`.
- `SendEmailParams` builder with the API's field names, and `Attachment` (raw bytes or base64).
- Idempotency keys reused across retries, and `Retry-After` honoured.
- Typed, unchecked exceptions that carry the request id.
- `Webhooks.verify`, with secret rotation.
