# Changelog

## 0.2.0 (unreleased)

- Audiences and contacts, for API keys with Full access: `Audiences.ListAsync`, `CreateAsync` and `RenameAsync`; `Contacts.ListAsync`, `AddAsync`, `UpsertAsync`, `UpsertBatchAsync`, `GetAsync`, `UpdateAsync`, `UnsubscribeAsync` and `DeleteAsync`.
- `ContactInput.DoubleOptIn` emails the person a confirmation link; they're subscribed only if they confirm. Someone who unsubscribed or reported spam can't be re-subscribed by these calls (a `ConflictException` with code `contact_unsubscribed`).
- Properties left `null` aren't sent, so an update keeps them; set a name to `""` to clear it.

## 0.1.0 (unreleased)

- `MailhiveClient`, with `Emails.SendAsync`, `Emails.SendBatchAsync` and `Emails.GetAsync`.
- Targets .NET 8 and .NET Standard 2.0 (so .NET Framework 4.6.2+ too).
- Idempotency keys reused across retries, and `Retry-After` honoured.
- Typed exceptions that carry the status, code, details and request id.
- `MailhiveWebhook.Verify`, with secret rotation.
