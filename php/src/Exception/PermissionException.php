<?php

declare(strict_types=1);

namespace Mailhive\Exception;

/** 403: Send isn't activated, or the stream is paused. */
class PermissionException extends ApiException
{
}
