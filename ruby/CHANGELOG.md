# Changelog

## 0.3.0 (unreleased)

- `rejected` on accepted and retrieved emails: the addresses dropped before sending because they can't work (a mistyped, non-existent or mail-less domain), mapped to the reason. They never reach the sending engine, so they can't bounce.
- `emails.validate` checks addresses on a form before sending: syntax, mistyped domains (with the address probably meant), domains that don't exist or have no mail server, and your suppression list. Nothing is sent or stored.

## 0.2.0 (2026-10-26)

- Audiences and contacts, for API keys with Full access: `audiences.list`, `create` and `rename`; `contacts.list`, `add`, `upsert`, `upsert_batch`, `get`, `update`, `unsubscribe` and `delete`.
- `double_opt_in` on a contact emails the person a confirmation link; they're subscribed only if they confirm. Someone who unsubscribed or reported spam can't be re-subscribed by these calls (a `Mailhive::ConflictError` with code `contact_unsubscribed`).
- Requests can now carry query strings, and an empty response (`204`) is `nil` from `contacts.delete`.

## 0.1.0 (2026-09-30)

- `Mailhive::Client`, with `emails.send`, `emails.send_batch` and `emails.get`.
- Idempotency keys reused across retries, and `Retry-After` honoured.
- Typed errors that carry the request id.
- `Mailhive::Webhook.verify`, with secret rotation.
- The ActionMailer delivery method (`:mailhive`), registered by a Railtie.
