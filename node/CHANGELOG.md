# Changelog

## 0.3.0 (unreleased)

- `rejected` on accepted and retrieved emails: addresses dropped before sending because they can't work (a mistyped, non-existent or mail-less domain), keyed by address with the reason. They never reach the sending engine, so they can't bounce.
- `emails.validate` runs the same checks for a form, before sending: syntax, mistyped domains with a suggestion (`ada@gmail.com` for `ada@gmial.com`), domains that don't exist or have no mail server, and your suppression list. Nothing is sent or stored.
- The Nodemailer transport lists those addresses in `info.rejected`, with the suppressed ones, and the reasons in `info.rejectedReasons`.

## 0.2.0 (2026-10-26)

- Audiences and contacts, for API keys with Full access: `audiences.list`, `create` and `rename`; `contacts.list`, `add`, `upsert`, `upsertBatch`, `get`, `update`, `unsubscribe` and `delete`.
- `double_opt_in` on a contact emails the person a confirmation link; they're subscribed only if they confirm. Someone who unsubscribed or reported spam can't be re-subscribed by these calls (a `ConflictError` with code `contact_unsubscribed`).
- Requests can now be `PUT`, `PATCH` and `DELETE` with query strings. Only `POST` carries an idempotency key.

## 0.1.0 (2026-09-29)

- `emails.send`, `emails.sendBatch` and `emails.get`.
- Automatic idempotency keys, reused across retries. Retries honour `Retry-After`.
- Typed errors that carry `requestId`.
- `verifyWebhook`, with support for secret rotation.
- The Nodemailer transport (`@mailhive/send/nodemailer`).
- Refuses secret keys in browsers.
