# Changelog

## 0.1.0 (unreleased)

- `Mailhive`, with `emails->send`, `emails->sendBatch` and `emails->get`.
- Idempotency keys reused across retries, and `Retry-After` honoured.
- Typed exceptions that carry the request id.
- `Webhook::verify`, with secret rotation.
- The Laravel mail driver (package discovery) and the Symfony Mailer transport.
