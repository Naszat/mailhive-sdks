# @mailhive/client

Send email from your website's forms with [Mailhive Send](https://mailhive.africa/docs/send/forms). You don't need a backend. It works on static sites, Webflow-style builders, single-page apps and anything else that runs in a browser.

You create the form in **Mailhive Send → Forms**, which fixes who receives its email, what it looks like and which fields it takes. Your page gets a **publishable key** (`mhp_…`). The key is safe to publish, because all it can do is send that form to your own inbox, within limits you control. Every submission passes an anti-spam check automatically.

## Drop-in script (no code)

```html
<script src="https://cdn.jsdelivr.net/npm/@mailhive/client@0/dist/embed.js" defer></script>

<form data-mailhive="mhp_your_form_key" data-mailhive-success="Thanks! We'll be in touch.">
  <input name="name" required>
  <input name="email" type="email" required>
  <span data-mailhive-error-for="email" hidden></span>
  <textarea name="message" required></textarea>
  <button>Send</button>
</form>
```

The script:
- adds a hidden honeypot field;
- starts the anti-spam check when someone begins typing;
- shows each field's error in `[data-mailhive-error-for="…"]` (or at the end of the form);
- replaces the form with the thank-you message, or goes to the form's redirect address if it has one.

To handle the result yourself, listen for `mailhive:success` or `mailhive:error` on the form and call `event.preventDefault()`. For forms added after the page loads, call `MailhiveForms.scan()`.

## JavaScript

```sh
npm install @mailhive/client
```

```js
import { createForm, FormError } from "@mailhive/client";

const form = createForm("mhp_your_form_key");
form.prepare(); // optional: start the anti-spam check early

try {
  await form.submit({ name: "Ada", email: "ada@example.com", message: "Hello" });
} catch (error) {
  if (error instanceof FormError) {
    console.log(error.code, error.message, error.fieldErrors);
  }
}
```

## React

```jsx
import { useMailhiveForm } from "@mailhive/client/react";

function Contact() {
  const form = useMailhiveForm("mhp_your_form_key");
  if (form.status === "success") return <p>Thanks!</p>;
  return (
    <form
      onFocus={form.prepare}
      onSubmit={(e) => {
        e.preventDefault();
        form.submit(Object.fromEntries(new FormData(e.currentTarget)));
      }}
    >
      <input name="email" type="email" aria-invalid={!!form.fieldError("email")} />
      {form.fieldError("email") && <span>{form.fieldError("email")}</span>}
      <button disabled={form.status === "submitting"}>Send</button>
      {form.status === "error" && !form.error?.fieldErrors.length && <p role="alert">{form.error?.message}</p>}
    </form>
  );
}
```

## Cloudflare Turnstile

If you've set the form to use your own Turnstile widget (in **Mailhive Send → Forms**), add the widget inside the form as usual. Its token is sent automatically, and the drop-in script resets the widget after each attempt:

```html
<script src="https://challenges.cloudflare.com/turnstile/v0/api.js" async defer></script>
<form data-mailhive="mhp_your_form_key">
  …
  <div class="cf-turnstile" data-sitekey="0x4AAAAAAA…"></div>
  <button>Send</button>
</form>
```

With the JavaScript API, the token is taken from the `cf-turnstile-response` value, or you can pass it yourself: `form.submit(values, { turnstileToken })`.

## Signed-in users

For a form that only your signed-in users can send, pass their ID token from your auth provider:

```js
// Firebase
await form.submit(values, { idToken: await auth.currentUser.getIdToken() });
// Supabase
await form.submit(values, { idToken: (await supabase.auth.getSession()).data.session.access_token });
// Clerk
await form.submit(values, { idToken: await getToken() });
// Auth0
await form.submit(values, { idToken: (await auth0.getIdTokenClaims()).__raw });
```

The React hook's `submit` takes the same options.

## Errors

| `code` | Meaning |
|---|---|
| `invalid_submission` | A field is missing, too long or the wrong type. See `fieldErrors` |
| `origin_not_allowed` | This site's address isn't in the form's allowed origins |
| `challenge_failed` | The anti-spam check failed. Reload the page and try again |
| `rate_limited` | Too many submissions. Wait `retryAfter` seconds |
| `form_paused` | The form's owner paused it |
| `form_unavailable` | The form can't send right now |
| `turnstile_missing` | The Turnstile widget hasn't been completed yet |
| `sign_in_required`, `token_expired`, `token_invalid`, `email_not_verified` | Signed-in forms: no valid sign-in was sent |
| `network_error` | The server couldn't be reached |

## Never use a secret key here

Secret API keys (`mhs_…`) can send anything as your domains, so this library refuses them. Only use the form's `mhp_` key in a web page.
