<?php

declare(strict_types=1);

namespace Mailhive\Exception;

/**
 * An error response from the API. Check errorCode(), not the message:
 * messages are written for people and may change.
 */
class ApiException extends MailhiveException
{
    /** @param array<string, string> $headers lower-cased names */
    public function __construct(
        string $message,
        private readonly int $status,
        private readonly string $errorCode,
        private readonly mixed $details = null,
        private readonly ?string $requestId = null,
        private readonly array $headers = [],
    ) {
        parent::__construct($message, $status);
    }

    public function status(): int
    {
        return $this->status;
    }

    /** Mailhive's stable error code, e.g. "domain_not_verified". */
    public function errorCode(): string
    {
        return $this->errorCode;
    }

    public function details(): mixed
    {
        return $this->details;
    }

    /** Quote this when contacting support. */
    public function requestId(): ?string
    {
        return $this->requestId;
    }

    /** @return array<string, string> */
    public function headers(): array
    {
        return $this->headers;
    }
}
