<?php

declare(strict_types=1);

namespace Mailhive\Http;

/** No response: the connection failed or timed out. */
final class TransportException extends \RuntimeException
{
    public function __construct(string $message, public readonly bool $timedOut = false)
    {
        parent::__construct($message);
    }
}
