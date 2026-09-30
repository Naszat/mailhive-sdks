# Changelog

## 0.1.0 (unreleased)

- `MailhiveClient`, with `Emails.SendAsync`, `Emails.SendBatchAsync` and `Emails.GetAsync`.
- Targets .NET 8 and .NET Standard 2.0 (so .NET Framework 4.6.2+ too).
- Idempotency keys reused across retries, and `Retry-After` honoured.
- Typed exceptions that carry the status, code, details and request id.
- `MailhiveWebhook.Verify`, with secret rotation.
