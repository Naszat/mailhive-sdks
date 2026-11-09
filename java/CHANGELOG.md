# Changelog

## 0.3.0 (unreleased)

- `rejected()` on accepted and retrieved emails: addresses dropped before sending because they can't work (a mistyped, non-existent or mail-less domain), keyed by address with the reason. They never reach the sending engine, so they can't bounce.
- `emails().validate(...)` checks addresses before sending, for a signup or checkout form while the person can still fix a typo: syntax, mistyped domains (with the address probably meant), domains that don't exist or have no mail server, and your suppression list. Nothing is sent or stored.

## 0.2.0 (2026-10-26)

- Audiences and contacts, for API keys with Full access: `audiences().list`, `create` and `rename`; `contacts().list`, `add`, `upsert`, `upsertBatch`, `get`, `update`, `unsubscribe` and `delete`.
- `DoubleOptIn` on a contact emails the person a confirmation link; they're subscribed only if they confirm. Someone who unsubscribed or reported spam can't be re-subscribed by these calls (a `ConflictException` with code `contact_unsubscribed`).
- `ContactInput` and `ContactUpdate` builders leave out what you don't set, so an update keeps it; set a name to `""` to clear it.

## 0.1.0 (2026-09-30)

- `Mailhive`, with `emails().send`, `emails().sendBatch` and `emails().get`.
- `SendEmailParams` builder with the API's field names, and `Attachment` (raw bytes or base64).
- Idempotency keys reused across retries, and `Retry-After` honoured.
- Typed, unchecked exceptions that carry the request id.
- `Webhooks.verify`, with secret rotation.
