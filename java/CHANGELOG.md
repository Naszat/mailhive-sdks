# Changelog

## 0.2.0 (unreleased)

- Audiences and contacts, for API keys with Full access: `audiences().list`, `create` and `rename`; `contacts().list`, `add`, `upsert`, `upsertBatch`, `get`, `update`, `unsubscribe` and `delete`.
- `DoubleOptIn` on a contact emails the person a confirmation link; they're subscribed only if they confirm. Someone who unsubscribed or reported spam can't be re-subscribed by these calls (a `ConflictException` with code `contact_unsubscribed`).
- `ContactInput` and `ContactUpdate` builders leave out what you don't set, so an update keeps it; set a name to `""` to clear it.

## 0.1.0 (unreleased)

- `Mailhive`, with `emails().send`, `emails().sendBatch` and `emails().get`.
- `SendEmailParams` builder with the API's field names, and `Attachment` (raw bytes or base64).
- Idempotency keys reused across retries, and `Retry-After` honoured.
- Typed, unchecked exceptions that carry the request id.
- `Webhooks.verify`, with secret rotation.
