<?php

declare(strict_types=1);

namespace Mailhive\Exception;

/**
 * 429: "rate_limited" (retried for you), or "monthly_quota_reached" /
 * "daily_cap_reached" (not retried: waiting won't help).
 */
class RateLimitException extends ApiException
{
    /** Seconds to wait, from Retry-After, if given. */
    public function retryAfter(): ?float
    {
        $value = $this->headers()['retry-after'] ?? null;
        return is_numeric($value) ? (float) $value : null;
    }
}
