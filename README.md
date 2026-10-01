# Mailhive SDKs

Official client libraries for [Mailhive Send](https://mailhive.africa/docs/send/overview), the transactional and broadcast email API from [Mailhive](https://mailhive.africa).

| Language | Package | Status |
|---|---|---|
| Node.js / TypeScript | [`@mailhive/send`](node/) | 0.1 on npm |
| Browser forms (no backend) | [`@mailhive/client`](client/) | 0.2 on npm |
| Python (and Django) | [`mailhive`](python/) | 0.1 on PyPI |
| PHP (and Laravel) | [`mailhive/mailhive-php`](php/) | 0.1 on Packagist |
| Go | [`github.com/Naszat/mailhive-sdks/go`](go/) | 0.1 (Go module proxy) |
| Java / Kotlin | [`africa.mailhive:mailhive-java`](java/) | 0.1 on Maven Central |
| .NET | [`Mailhive`](dotnet/) | 0.1 on NuGet |
| Ruby (and Rails) | [`mailhive`](ruby/) | 0.1 on RubyGems |

## How the SDKs stay consistent

Every SDK must behave the same way. So they share one specification, kept in [`spec/`](spec/):

- **`spec/openapi.json`** is a copy of the published API description at `https://mailhive.africa/docs/mailhive-send-openapi.json`. A scheduled CI job reports when the two differ.
- **`spec/conformance.json`** lists the behaviour every SDK must pass: headers, retries, idempotency and how errors are mapped. Each case runs against the **mock server** in [`mock-server/`](mock-server/). The API key chooses the scenario, and the server records every request.
- **`spec/pow-vectors.json`** holds anti-spam challenges issued by the backend, with their solutions. The browser library's solver must solve every one.
- **`spec/webhook-vectors.json`** holds webhook signatures made with the Mailhive backend's own signing function. Every verifier must accept the valid ones and reject the invalid ones.

## Standards every SDK follows

- Field names match the REST API exactly (`template_id`, `reply_to`), so the API reference works for every language.
- The key comes from an argument or from `MAILHIVE_API_KEY`. The base URL can be overridden (`MAILHIVE_BASE_URL`), for beta.
- **Retries never send twice.** Each send gets one `Idempotency-Key` (generated if you don't pass one), and every retry reuses it.
  - Retried: network errors, 5xx and `429 rate_limited`. The SDK honours `Retry-After`, or backs off exponentially with jitter. There are at most 2 retries by default.
  - Not retried: `429 monthly_quota_reached` and `daily_cap_reached`, because waiting doesn't help.
- **Typed errors,** each carrying `status`, `code`, `message`, `details` and `request_id`:
  - `api` → base class
  - `authentication` → 401
  - `billing` → 402
  - `permission` → 403
  - `not_found` → 404
  - `conflict` → 409
  - `validation` → 422
  - `rate_limit` → 429
  - `connection` → no response
- **Webhook verification:** the SDK checks `Mailhive-Signature` (`t=…,v1=…`, HMAC-SHA256 of `"<t>.<raw body>"`) with a constant-time comparison and a 300-second tolerance. It accepts several `v1` values, so secrets can be rotated.
- **Keys:**
  - Keys are never logged or printed.
  - Secret keys (`mhs_…`) are refused in browsers.
  - Test keys (`mhs_test_…`) behave the same as live keys, but delivery is simulated.
- **User-Agent:** `mailhive-<language>/<version> <runtime>/<version>`.

## Running the mock server

```sh
node mock-server/server.mjs --port 4010
# base URL: http://127.0.0.1:4010/v1, API key: mhs_<scenario>
```

## Releasing

Each SDK is tagged and published on its own from `main`:

| SDK | Tag | Where it goes |
|---|---|---|
| Node | `node-vX.Y.Z` | npm, through trusted publishing |
| Browser client | `client-vX.Y.Z` | npm, through trusted publishing |
| Python | `python-vX.Y.Z` | PyPI, through trusted publishing |
| PHP | `php-vX.Y.Z` | The `Naszat/mailhive-php` mirror, then Packagist |
| Go | `go/vX.Y.Z` | The Go module proxy (no workflow needed) |
| Java | `java-vX.Y.Z` | Maven Central |
| .NET | `dotnet-vX.Y.Z` | NuGet, through trusted publishing |
| Ruby | `ruby-vX.Y.Z` | RubyGems, through trusted publishing |

The one-time setup for each registry is in comments at the top of its `release-*.yml` workflow.
