# Changelog

## 0.1.0 (unreleased)

- `createForm(key).submit(values)` solves the anti-spam proof of work and waits until the challenge is old enough.
- Typed `FormError`, with field errors and `retryAfter`.
- The drop-in `embed.js` for plain HTML forms, with a honeypot, field errors, the success message and events.
- `useMailhiveForm` for React.
- Refuses secret keys.
