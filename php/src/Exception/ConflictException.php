<?php

declare(strict_types=1);

namespace Mailhive\Exception;

/** 409: e.g. an Idempotency-Key reused for a different request. */
class ConflictException extends ApiException
{
}
