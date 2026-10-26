# Changelog

## 0.2.0 (2026-10-26)

- Audiences and contacts, for API keys with Full access: `Audiences.List`, `Create` and `Rename`; `Contacts.List`, `Add`, `Upsert`, `UpsertBatch`, `Get`, `Update`, `Unsubscribe` and `Delete`.
- `DoubleOptIn` on a contact emails the person a confirmation link; they're subscribed only if they confirm. Someone who unsubscribed or reported spam can't be re-subscribed by these calls (a `*ConflictError` with code `contact_unsubscribed`).
- `ContactInput` and `ContactUpdate` take `*string` names (nil keeps, `""` clears); use `mailhive.String`.

## 0.1.0 (2026-09-30)

- `mailhive.New`, with `Emails.Send`, `Emails.SendBatch` and `Emails.Get`.
- Idempotency keys reused across retries, `Retry-After` honoured, and exponential backoff with jitter.
- Typed errors (`*APIError` and one type per well-known status) that carry the request id; `*ConnectionError` when there's no answer.
- `VerifyWebhook`, with secret rotation.
- Standard library only; Go 1.21+.
