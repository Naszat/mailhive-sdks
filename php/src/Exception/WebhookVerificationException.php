<?php

declare(strict_types=1);

namespace Mailhive\Exception;

/** A webhook's signature didn't check out. reason(): header, timestamp or signature. */
class WebhookVerificationException extends MailhiveException
{
    public function __construct(string $message, private readonly string $reason)
    {
        parent::__construct($message);
    }

    public function reason(): string
    {
        return $this->reason;
    }
}
