# Changelog

## 0.2.0 (unreleased)

- Follows each form's anti-spam mode from the server. The proof of work runs only for forms that use it.
- Cloudflare Turnstile forms: the widget's `cf-turnstile-response` is sent, or pass `turnstileToken`. The embed resets the widget after each attempt.
- Signed-in user forms: `submit(values, { idToken })`, which the React hook's `submit` also takes.

## 0.1.0 (unreleased)

- `createForm(key).submit(values)` solves the anti-spam proof of work and waits until the challenge is old enough.
- Typed `FormError`, with field errors and `retryAfter`.
- The drop-in `embed.js` for plain HTML forms, with a honeypot, field errors, the success message and events.
- `useMailhiveForm` for React.
- Refuses secret keys.
