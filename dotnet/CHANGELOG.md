# Changelog

## 0.3.0 (2026-11-09)

- `Rejected` on accepted and retrieved emails: addresses dropped before sending because they can't work (a mistyped domain such as gmial.com, a domain that doesn't exist, or one with no mail server), keyed by address with the reason. They never reach the sending engine, so they can't bounce.
- `Emails.ValidateAsync` runs the same checks on addresses typed into a form, before anything is sent: syntax, mistyped domains (with the address probably meant as `Suggestion`), domains that don't exist or accept no email, and your suppression list. Nothing is sent or stored.

## 0.2.0 (2026-10-26)

- Audiences and contacts, for API keys with Full access: `Audiences.ListAsync`, `CreateAsync` and `RenameAsync`; `Contacts.ListAsync`, `AddAsync`, `UpsertAsync`, `UpsertBatchAsync`, `GetAsync`, `UpdateAsync`, `UnsubscribeAsync` and `DeleteAsync`.
- `ContactInput.DoubleOptIn` emails the person a confirmation link; they're subscribed only if they confirm. Someone who unsubscribed or reported spam can't be re-subscribed by these calls (a `ConflictException` with code `contact_unsubscribed`).
- Properties left `null` aren't sent, so an update keeps them; set a name to `""` to clear it.

## 0.1.0 (2026-09-30)

- `MailhiveClient`, with `Emails.SendAsync`, `Emails.SendBatchAsync` and `Emails.GetAsync`.
- Targets .NET 8 and .NET Standard 2.0 (so .NET Framework 4.6.2+ too).
- Idempotency keys reused across retries, and `Retry-After` honoured.
- Typed exceptions that carry the status, code, details and request id.
- `MailhiveWebhook.Verify`, with secret rotation.
