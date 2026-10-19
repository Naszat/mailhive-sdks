# Changelog

## 0.2.0 (unreleased)

- Audiences and contacts, for API keys with Full access: `audiences.list`, `create` and `rename`; `contacts.list`, `add`, `upsert`, `upsert_batch`, `get`, `update`, `unsubscribe` and `delete`.
- `double_opt_in` on a contact emails the person a confirmation link; they're subscribed only if they confirm. Someone who unsubscribed or reported spam can't be re-subscribed by these calls (a `Mailhive::ConflictError` with code `contact_unsubscribed`).
- Requests can now carry query strings, and an empty response (`204`) is `nil` from `contacts.delete`.

## 0.1.0 (unreleased)

- `Mailhive::Client`, with `emails.send`, `emails.send_batch` and `emails.get`.
- Idempotency keys reused across retries, and `Retry-After` honoured.
- Typed errors that carry the request id.
- `Mailhive::Webhook.verify`, with secret rotation.
- The ActionMailer delivery method (`:mailhive`), registered by a Railtie.
