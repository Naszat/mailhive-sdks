<?php

declare(strict_types=1);

namespace Mailhive\Exception;

/** 402: Mailhive Send is paused over an unpaid invoice. */
class BillingException extends ApiException
{
}
