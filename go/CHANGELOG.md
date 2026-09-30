# Changelog

## 0.1.0 (unreleased)

- `mailhive.New`, with `Emails.Send`, `Emails.SendBatch` and `Emails.Get`.
- Idempotency keys reused across retries, `Retry-After` honoured, and exponential backoff with jitter.
- Typed errors (`*APIError` and one type per well-known status) that carry the request id; `*ConnectionError` when there's no answer.
- `VerifyWebhook`, with secret rotation.
- Standard library only; Go 1.21+.
