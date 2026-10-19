# Changelog

## 0.2.0 (unreleased)

- Audiences and contacts, for API keys with Full access: `audiences.list`, `create` and `rename`; `contacts.list`, `add`, `upsert`, `upsert_batch`, `get`, `update`, `unsubscribe` and `delete`. Each is also on `AsyncMailhive`.
- `double_opt_in` on a contact emails the person a confirmation link; they're subscribed only if they confirm. Someone who unsubscribed or reported spam can't be re-subscribed by these calls (a `ConflictError` with code `contact_unsubscribed`).
- Requests can now be `PUT`, `PATCH` and `DELETE` with query strings, and an empty response (`204`) is `None`. Only `POST` carries an idempotency key.

## 0.1.0 (unreleased)

- `Mailhive` and `AsyncMailhive`, with `emails.send`, `emails.send_batch` and `emails.get`.
- Idempotency keys reused across retries, and `Retry-After` honoured.
- Typed errors that carry `request_id`.
- `verify_webhook`, with secret rotation.
- The Django email backend `mailhive.django.EmailBackend`.
