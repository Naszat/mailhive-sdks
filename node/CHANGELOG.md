# Changelog

## 0.2.0 (unreleased)

- Audiences and contacts, for API keys with Full access: `audiences.list`, `create` and `rename`; `contacts.list`, `add`, `upsert`, `upsertBatch`, `get`, `update`, `unsubscribe` and `delete`.
- `double_opt_in` on a contact emails the person a confirmation link; they're subscribed only if they confirm. Someone who unsubscribed or reported spam can't be re-subscribed by these calls (a `ConflictError` with code `contact_unsubscribed`).
- Requests can now be `PUT`, `PATCH` and `DELETE` with query strings. Only `POST` carries an idempotency key.

## 0.1.0 (unreleased)

- `emails.send`, `emails.sendBatch` and `emails.get`.
- Automatic idempotency keys, reused across retries. Retries honour `Retry-After`.
- Typed errors that carry `requestId`.
- `verifyWebhook`, with support for secret rotation.
- The Nodemailer transport (`@mailhive/send/nodemailer`).
- Refuses secret keys in browsers.
