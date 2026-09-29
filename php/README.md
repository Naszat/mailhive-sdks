# mailhive/mailhive-php

The official PHP SDK for [Mailhive Send](https://mailhive.africa/docs/send/overview). It needs PHP 8.1+ and only `ext-curl` and `ext-json`. It includes a **Laravel** mail driver.

```sh
composer require mailhive/mailhive-php
```

## Send an email

```php
use Mailhive\Mailhive;

$mailhive = new Mailhive(); // reads MAILHIVE_API_KEY

$email = $mailhive->emails->send([
    'from' => 'Acme <hello@acme.com>',
    'to' => 'ada@example.com',
    'subject' => 'Your receipt',
    'html' => '<p>Thanks for your order.</p>',
]);
echo $email['id'];
```

The keys match the [API reference](https://mailhive.africa/docs/api/send-email) exactly: `cc`, `bcc`, `reply_to`, `headers`, `tags`, `template_id`, `variables` and `attachments`.

An attachment's `content` is the raw file, which is encoded for you. Use `content_base64` if your data is already base64:

```php
'attachments' => [
    ['filename' => 'invoice.pdf', 'content' => file_get_contents('invoice.pdf'), 'content_type' => 'application/pdf'],
],
```

```php
$mailhive->emails->sendBatch([$email1, $email2]); // up to 100; all accepted or none
$mailhive->emails->get($id);                      // ['status' => 'delivered', …]
```

## Laravel

The package registers itself. Add the key and the mailer:

```php
// config/services.php
'mailhive' => ['key' => env('MAILHIVE_API_KEY')],

// config/mail.php, under 'mailers'
'mailhive' => ['transport' => 'mailhive'],
```

```dotenv
MAIL_MAILER=mailhive
MAILHIVE_API_KEY=mhs_…
```

Every Mailable and notification then sends through Mailhive, including attachments, cc, bcc and Reply-To. A Mailable's `tags` and `metadata` become Mailhive tags. To make a send safe to repeat, add an `X-Mailhive-Idempotency-Key` header:

```php
public function headers(): Headers
{
    return new Headers(text: ['X-Mailhive-Idempotency-Key' => "order-{$this->order->id}-receipt"]);
}
```

You can also type-hint the client anywhere: `public function __construct(private \Mailhive\Mailhive $mailhive) {}`.

Plain Symfony apps can use `new \Mailhive\Laravel\MailhiveTransport($client)` as a Symfony Mailer transport.

## Retries and idempotency

Every send carries an `Idempotency-Key`. Retries reuse it, so **a retry never sends twice**.

- **Retried** (up to 2 times, then configurable): network errors, timeouts, 5xx responses, and `429 rate_limited` after the `Retry-After` delay.
- **Not retried:** a used-up allowance and invalid requests.

To stay safe across restarts, pass your own key:

```php
$mailhive->emails->send($email, ['idempotency_key' => "order-{$order->id}-receipt"]);
```

## Errors

```php
use Mailhive\Exception\RateLimitException;
use Mailhive\Exception\ValidationException;

try {
    $mailhive->emails->send($email);
} catch (ValidationException $e) {
    echo $e->errorCode(), $e->getMessage(), json_encode($e->details()), $e->requestId();
} catch (RateLimitException $e) {
    // $e->errorCode() === 'monthly_quota_reached', or $e->retryAfter()
}
```

| Exception | Status |
|---|---|
| `AuthenticationException` | 401 |
| `BillingException` | 402 |
| `PermissionException` | 403 |
| `NotFoundException` | 404 |
| `ConflictException` | 409 |
| `ValidationException` | 422 |
| `RateLimitException` | 429 |
| `ApiException` | other statuses, and the base class of all of the above |
| `ConnectionException` | no response |

All of them extend `MailhiveException`. Use `errorCode()` for Mailhive's code: PHP reserves `getCode()` for the HTTP status.

## Webhooks

```php
use Mailhive\Exception\WebhookVerificationException;
use Mailhive\Webhook;

try {
    $event = Webhook::verify(
        file_get_contents('php://input'),   // Laravel: $request->getContent()
        $_SERVER['HTTP_MAILHIVE_SIGNATURE'] ?? null,  // Laravel: $request->header('Mailhive-Signature')
        getenv('MAILHIVE_WEBHOOK_SECRET'),
    );
} catch (WebhookVerificationException $e) {
    http_response_code(400);
    exit;
}
```

## Options

```php
new Mailhive('mhs_…', [
    'base_url' => 'https://api-beta.mailhive.africa/v1', // default: MAILHIVE_BASE_URL, then production
    'timeout' => 30.0,
    'max_retries' => 2,
]);
```

**Test keys** (`mhs_test_…`) work unchanged: delivery is simulated and nothing is billed.

This package is published from the [mailhive-sdks](https://github.com/Naszat/mailhive-sdks) monorepo. Please open issues and pull requests there.
