# @mailhive/send

The official Node.js and TypeScript SDK for [Mailhive Send](https://mailhive.africa/docs/send/overview). It has no runtime dependencies, and works on Node 20.3+, Bun, Deno, Cloudflare Workers and Vercel Edge.

```sh
npm install @mailhive/send
```

## Send an email

```ts
import { Mailhive } from "@mailhive/send";

const mailhive = new Mailhive(process.env.MAILHIVE_API_KEY);

const { id } = await mailhive.emails.send({
  from: "Acme <hello@acme.com>",
  to: "ada@example.com",
  subject: "Your receipt",
  html: "<p>Thanks for your order.</p>",
});
```

With no argument, `new Mailhive()` reads `MAILHIVE_API_KEY`. Create keys under **Mailhive Send → API keys**. The `from` address must be on a [verified sending domain](https://mailhive.africa/docs/send/sending-domains).

Field names match the [API reference](https://mailhive.africa/docs/api/send-email) exactly: `cc`, `bcc`, `reply_to`, `headers`, `tags`, `template_id`, `variables`, `attachments`.

### With a template

```ts
await mailhive.emails.send({
  from: "hello@acme.com",
  to: "ada@example.com",
  template_id: "4c1d…",
  variables: { first_name: "Ada", order_number: 1042 },
});
```

### Attachments

Pass bytes (a `Buffer` or `Uint8Array`) or base64 text:

```ts
await mailhive.emails.send({
  from: "hello@acme.com",
  to: "ada@example.com",
  subject: "Invoice",
  text: "Attached.",
  attachments: [{ filename: "invoice.pdf", content: await readFile("invoice.pdf"), content_type: "application/pdf" }],
});
```

### Batch

This sends up to 100 independent emails in one request. All of them are accepted, or none are:

```ts
const accepted = await mailhive.emails.sendBatch([email1, email2]);
```

### Look one up

```ts
const email = await mailhive.emails.get(id); // email.status: "delivered", "bounced", …
```

## Retries and idempotency

Every send carries an `Idempotency-Key`. Retries reuse it, so **a retry never sends twice**.

- **Retried:**
  - network errors and timeouts;
  - 5xx responses;
  - `429 rate_limited`, after the `Retry-After` delay.
- **Default:** up to 2 retries. Change it with `maxRetries`.
- **Not retried:** a used-up allowance (`monthly_quota_reached`, `daily_cap_reached`) or an invalid request.

Pass your own key to make a send safe across restarts of your process:

```ts
await mailhive.emails.send(email, { idempotencyKey: `order-${order.id}-receipt` });
```

## Errors

Every error is a `MailhiveError`. API errors carry `status`, `code`, `message`, `details` and `requestId`. Quote the `requestId` when you contact support.

```ts
import { RateLimitError, ValidationError } from "@mailhive/send";

try {
  await mailhive.emails.send(email);
} catch (error) {
  if (error instanceof ValidationError && error.code === "domain_not_verified") {
    // verify the domain first
  } else if (error instanceof RateLimitError && error.code === "monthly_quota_reached") {
    // upgrade, or wait for the next period
  } else {
    throw error;
  }
}
```

| Class | Status | Examples |
|---|---|---|
| `AuthenticationError` | 401 | `invalid_api_key` |
| `BillingError` | 402 | `billing_restricted` |
| `PermissionError` | 403 | `send_not_activated`, `sending_paused` |
| `NotFoundError` | 404 | `not_found` |
| `ConflictError` | 409 | `idempotency_conflict` |
| `ValidationError` | 422 | `validation_error`, `domain_not_verified`, `template_variables` |
| `RateLimitError` | 429 | `rate_limited`, `monthly_quota_reached`, `daily_cap_reached` |
| `ApiError` | other | `internal_server_error` |
| `ConnectionError` | none | The API couldn't be reached, or it timed out |

## Test keys

A test key (`mhs_test_…`) goes through every check a live key does, but delivery is simulated and nothing is billed. Use test keys in development and CI. To choose the outcome, send to `delivered@`, `bounced@`, `complained@` or `delayed@simulator.mailhive.africa`. [More about test keys](https://mailhive.africa/docs/send/api-keys#test-keys).

## Webhooks

Verify every webhook before trusting it. `verifyWebhook` needs the **raw** body, exactly as received:

```ts
import express from "express";
import { verifyWebhook, WebhookVerificationError } from "@mailhive/send";

app.post("/webhooks/mailhive", express.raw({ type: "application/json" }), async (req, res) => {
  try {
    const event = await verifyWebhook({
      payload: req.body, // a Buffer, thanks to express.raw
      signature: req.header("Mailhive-Signature"),
      secret: process.env.MAILHIVE_WEBHOOK_SECRET!,
    });
    if (event.type === "email.bounced") {
      // …
    }
    res.sendStatus(200);
  } catch (error) {
    if (error instanceof WebhookVerificationError) return res.sendStatus(400);
    throw error;
  }
});
```

In a Next.js route handler, use `payload: await request.text()` and `signature: request.headers.get("Mailhive-Signature")`.

## Nodemailer

Already using Nodemailer? Swap the transport:

```ts
import nodemailer from "nodemailer";
import { mailhiveTransport } from "@mailhive/send/nodemailer";

const transporter = nodemailer.createTransport(mailhiveTransport()); // reads MAILHIVE_API_KEY
await transporter.sendMail({ from: "hello@acme.com", to: "ada@example.com", subject: "Hi", text: "Hello" });
```

You can add `idempotencyKey` and `tags` to `sendMail`'s options.

## Options

```ts
new Mailhive({
  apiKey: "mhs_…",                                 // default: MAILHIVE_API_KEY
  baseUrl: "https://api-beta.mailhive.africa/v1",  // default: MAILHIVE_BASE_URL, then production
  timeout: 30_000,                                 // per attempt, in ms
  maxRetries: 2,
  fetch: customFetch,
});
```

## Keep your key on the server

API keys can send email as your domains. The SDK refuses to run with a secret key in a browser. To send from a site with no backend, use [`@mailhive/client`](../client/) and a form's publishable key.
