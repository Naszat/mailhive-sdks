<?php

declare(strict_types=1);

namespace Mailhive\Exception;

/** 401: missing, unknown or revoked API key. */
class AuthenticationException extends ApiException
{
}
