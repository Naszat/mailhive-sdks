# Changelog

## 0.3.0 (2026-11-09)

- `rejected` on accepted and retrieved emails: the addresses dropped before sending because they can't work (a mistyped, non-existent or mail-less domain), each with the reason. They never reach the sending engine, so they can't bounce.
- `emails->validate` checks addresses before sending, for a signup or checkout form while the person can still fix a typo: syntax, mistyped domains (with the address probably meant as `suggestion`), domains that don't exist or have no mail server, and your suppression list. Nothing is sent or stored.

## 0.2.0 (2026-10-26)

- Audiences and contacts, for API keys with Full access: `audiences->list`, `create` and `rename`; `contacts->list`, `add`, `upsert`, `upsertBatch`, `get`, `update`, `unsubscribe` and `delete`.
- `double_opt_in` on a contact emails the person a confirmation link; they're subscribed only if they confirm. Someone who unsubscribed or reported spam can't be re-subscribed by these calls (a `ConflictException` with code `contact_unsubscribed`).
- Requests can now be `PUT`, `PATCH` and `DELETE` with query strings, and an empty response (`204`) is an empty array. Only `POST` carries an idempotency key.

## 0.1.0 (2026-09-29)

- `Mailhive`, with `emails->send`, `emails->sendBatch` and `emails->get`.
- Idempotency keys reused across retries, and `Retry-After` honoured.
- Typed exceptions that carry the request id.
- `Webhook::verify`, with secret rotation.
- The Laravel mail driver (package discovery) and the Symfony Mailer transport.
