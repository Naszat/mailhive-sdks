<?php

declare(strict_types=1);

namespace Mailhive;

use Mailhive\Exception\WebhookVerificationException;

final class Webhook
{
    /**
     * Checks a webhook's Mailhive-Signature header (HMAC-SHA256 of
     * "<t>.<raw body>" with the endpoint's secret) and returns the event.
     * $payload must be the raw body exactly as received:
     * file_get_contents('php://input'), or $request->getContent() in Laravel.
     * Several v1= values are accepted, so secrets can be rotated.
     *
     * @return array<string, mixed>
     * @throws WebhookVerificationException
     */
    public static function verify(string $payload, ?string $signature, string $secret, int $tolerance = 300, ?int $now = null): array
    {
        $timestamp = null;
        $candidates = [];
        foreach (explode(',', (string) $signature) as $part) {
            $pair = explode('=', $part, 2);
            if (count($pair) !== 2) {
                continue;
            }
            [$key, $value] = [trim($pair[0]), trim($pair[1])];
            if ($key === 't' && ctype_digit($value)) {
                $timestamp = (int) $value;
            } elseif ($key === 'v1' && preg_match('/^[0-9a-f]{64}$/i', $value) === 1) {
                $candidates[] = strtolower($value);
            }
        }
        if ($timestamp === null || $candidates === []) {
            throw new WebhookVerificationException('Missing or malformed Mailhive-Signature header.', 'header');
        }
        if (abs(($now ?? time()) - $timestamp) > $tolerance) {
            throw new WebhookVerificationException(
                "The webhook's timestamp is more than {$tolerance} seconds from now; it may be a replay.",
                'timestamp',
            );
        }
        $expected = hash_hmac('sha256', "{$timestamp}." . $payload, $secret);
        foreach ($candidates as $candidate) {
            if (hash_equals($expected, $candidate)) {
                return json_decode($payload, true, 512, JSON_THROW_ON_ERROR);
            }
        }
        throw new WebhookVerificationException(
            "The webhook's signature doesn't match. Check the endpoint's signing secret.",
            'signature',
        );
    }
}
