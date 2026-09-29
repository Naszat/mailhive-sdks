<?php

declare(strict_types=1);

namespace Mailhive;

use Mailhive\Exception\ApiException;
use Mailhive\Exception\AuthenticationException;
use Mailhive\Exception\BillingException;
use Mailhive\Exception\ConflictException;
use Mailhive\Exception\ConnectionException;
use Mailhive\Exception\MailhiveException;
use Mailhive\Exception\NotFoundException;
use Mailhive\Exception\PermissionException;
use Mailhive\Exception\RateLimitException;
use Mailhive\Exception\ValidationException;
use Mailhive\Http\CurlTransport;
use Mailhive\Http\Response;
use Mailhive\Http\Transport;
use Mailhive\Http\TransportException;

/**
 * The Mailhive Send API client.
 *
 *     $mailhive = new \Mailhive\Mailhive();   // reads MAILHIVE_API_KEY
 *     $mailhive->emails->send(['from' => 'hello@acme.com', 'to' => 'ada@example.com', 'subject' => 'Hi', 'text' => 'Hello']);
 */
final class Mailhive
{
    public const VERSION = '0.1.0';
    public const DEFAULT_BASE_URL = 'https://api.mailhive.africa/v1';
    private const MAX_RETRY_AFTER = 60.0;

    public readonly Emails $emails;
    public readonly string $baseUrl;
    private readonly string $apiKey;
    private readonly float $timeout;
    private readonly int $maxRetries;
    private readonly Transport $transport;
    /** @var \Closure(float): void */
    private \Closure $sleep;

    /**
     * @param array{base_url?: string, timeout?: float, max_retries?: int, transport?: Transport, sleep?: callable(float): void} $options
     */
    public function __construct(?string $apiKey = null, array $options = [])
    {
        $key = $apiKey ?? (getenv('MAILHIVE_API_KEY') ?: null);
        if ($key === null || $key === '') {
            throw new MailhiveException(
                'No API key. Pass one to new Mailhive(...) or set MAILHIVE_API_KEY. Create keys under Mailhive Send → API keys.'
            );
        }
        if (str_starts_with($key, 'mhp_')) {
            throw new MailhiveException(
                "That's a form's publishable key (mhp_…). The server SDK needs a secret API key (mhs_…) from Mailhive Send → API keys."
            );
        }
        $this->apiKey = $key;
        $this->baseUrl = rtrim($options['base_url'] ?? (getenv('MAILHIVE_BASE_URL') ?: self::DEFAULT_BASE_URL), '/');
        $this->timeout = (float) ($options['timeout'] ?? 30.0);
        $this->maxRetries = max(0, (int) ($options['max_retries'] ?? 2));
        $this->transport = $options['transport'] ?? new CurlTransport();
        $this->sleep = \Closure::fromCallable($options['sleep'] ?? static function (float $seconds): void {
            usleep((int) ($seconds * 1_000_000));
        });
        $this->emails = new Emails($this);
    }

    /** Never shows the key in var_dump() or print_r(). */
    public function __debugInfo(): array
    {
        return ['baseUrl' => $this->baseUrl, 'timeout' => $this->timeout, 'maxRetries' => $this->maxRetries];
    }

    /** @see Webhook::verify() */
    public static function verifyWebhook(string $payload, ?string $signature, string $secret, int $tolerance = 300, ?int $now = null): array
    {
        return Webhook::verify($payload, $signature, $secret, $tolerance, $now);
    }

    /**
     * @internal Used by the resources.
     * @param array<string, mixed>|null $body
     */
    public function request(string $method, string $path, ?array $body = null, ?string $idempotencyKey = null): array
    {
        $headers = [
            'Authorization' => "Bearer {$this->apiKey}",
            'Accept' => 'application/json',
            'User-Agent' => 'mailhive-php/' . self::VERSION . ' php/' . PHP_VERSION,
        ];
        $payload = null;
        if ($body !== null) {
            $headers['Content-Type'] = 'application/json';
            $payload = json_encode($body, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_THROW_ON_ERROR);
        }
        if ($method === 'POST') {
            // One key per call, reused on every retry: a retry never sends twice.
            $headers['Idempotency-Key'] = $idempotencyKey ?? self::uuid();
        }
        $url = $this->baseUrl . $path;

        for ($attempt = 0; ; $attempt++) {
            try {
                $response = $this->transport->send($method, $url, $headers, $payload, $this->timeout);
            } catch (TransportException $exception) {
                if ($attempt < $this->maxRetries) {
                    ($this->sleep)(self::backoff($attempt));
                    continue;
                }
                throw new ConnectionException(
                    $exception->timedOut
                        ? "The Mailhive API didn't answer within {$this->timeout} seconds."
                        : "Couldn't reach the Mailhive API at {$this->baseUrl}.",
                    0,
                    $exception,
                );
            }
            if ($response->status >= 200 && $response->status < 300) {
                return json_decode($response->body, true, 512, JSON_THROW_ON_ERROR);
            }
            $error = self::error($response);
            $wait = $this->waitBeforeRetry($error, $attempt);
            if ($wait === null) {
                throw $error;
            }
            ($this->sleep)($wait);
        }
    }

    private function waitBeforeRetry(ApiException $error, int $attempt): ?float
    {
        if ($attempt >= $this->maxRetries) {
            return null;
        }
        // Only a rate limit and server errors are worth retrying: a used-up
        // allowance or a bad request fails the same way again.
        if (!($error->status() >= 500 || ($error->status() === 429 && $error->errorCode() === 'rate_limited'))) {
            return null;
        }
        $retryAfter = $error->headers()['retry-after'] ?? null;
        if (is_numeric($retryAfter)) {
            $seconds = (float) $retryAfter;
            return $seconds > self::MAX_RETRY_AFTER ? null : max(0.0, $seconds);
        }
        return self::backoff($attempt);
    }

    private static function error(Response $response): ApiException
    {
        $decoded = json_decode($response->body, true);
        $error = is_array($decoded) && is_array($decoded['error'] ?? null) ? $decoded['error'] : [];
        $class = match ($response->status) {
            401 => AuthenticationException::class,
            402 => BillingException::class,
            403 => PermissionException::class,
            404 => NotFoundException::class,
            409 => ConflictException::class,
            400, 422 => ValidationException::class,
            429 => RateLimitException::class,
            default => ApiException::class,
        };
        return new $class(
            is_string($error['message'] ?? null) ? $error['message'] : "HTTP {$response->status}",
            $response->status,
            is_string($error['code'] ?? null) ? $error['code'] : 'http_error',
            $error['details'] ?? null,
            $error['request_id'] ?? ($response->headers['x-request-id'] ?? null),
            $response->headers,
        );
    }

    /** About 0.5s, 1s, 2s … up to 8s, with jitter. */
    private static function backoff(int $attempt): float
    {
        $ceiling = min(8.0, 0.5 * 2 ** $attempt);
        return $ceiling / 2 + (mt_rand() / mt_getrandmax()) * $ceiling / 2;
    }

    private static function uuid(): string
    {
        $bytes = random_bytes(16);
        $bytes[6] = chr((ord($bytes[6]) & 0x0f) | 0x40);
        $bytes[8] = chr((ord($bytes[8]) & 0x3f) | 0x80);
        return vsprintf('%s%s-%s-%s-%s-%s%s%s', str_split(bin2hex($bytes), 4));
    }
}
