# Changelog

## 0.1.0 (unreleased)

- `emails.send`, `emails.sendBatch` and `emails.get`.
- Automatic idempotency keys, reused across retries. Retries honour `Retry-After`.
- Typed errors that carry `requestId`.
- `verifyWebhook`, with support for secret rotation.
- The Nodemailer transport (`@mailhive/send/nodemailer`).
- Refuses secret keys in browsers.
