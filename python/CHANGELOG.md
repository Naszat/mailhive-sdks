# Changelog

## 0.3.0 (unreleased)

- `rejected` on accepted and retrieved emails: addresses dropped before sending because they can't work (a mistyped domain such as `gmial.com`, a domain that doesn't exist, or one with no mail server), keyed by address with the reason. They never reach the sending engine, so they can't bounce.
- `emails.validate` checks addresses before you send, for a signup or checkout form while the person can still fix a typo: syntax, mistyped domains (with the address probably meant as `suggestion`), domains that don't exist or have no mail server, and your suppression list. Nothing is sent or stored. Also on `AsyncMailhive`.

## 0.2.0 (2026-10-26)

- Audiences and contacts, for API keys with Full access: `audiences.list`, `create` and `rename`; `contacts.list`, `add`, `upsert`, `upsert_batch`, `get`, `update`, `unsubscribe` and `delete`. Each is also on `AsyncMailhive`.
- `double_opt_in` on a contact emails the person a confirmation link; they're subscribed only if they confirm. Someone who unsubscribed or reported spam can't be re-subscribed by these calls (a `ConflictError` with code `contact_unsubscribed`).
- Requests can now be `PUT`, `PATCH` and `DELETE` with query strings, and an empty response (`204`) is `None`. Only `POST` carries an idempotency key.

## 0.1.0 (2026-09-29)

- `Mailhive` and `AsyncMailhive`, with `emails.send`, `emails.send_batch` and `emails.get`.
- Idempotency keys reused across retries, and `Retry-After` honoured.
- Typed errors that carry `request_id`.
- `verify_webhook`, with secret rotation.
- The Django email backend `mailhive.django.EmailBackend`.
