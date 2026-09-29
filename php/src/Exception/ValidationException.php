<?php

declare(strict_types=1);

namespace Mailhive\Exception;

/** 422: the request is invalid; details() lists every problem. */
class ValidationException extends ApiException
{
}
