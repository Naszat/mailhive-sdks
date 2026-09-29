<?php

declare(strict_types=1);

namespace Mailhive\Tests;

final class Spec
{
    public static function load(string $name): array
    {
        return json_decode((string) file_get_contents(__DIR__ . '/../../spec/' . $name), true, 512, JSON_THROW_ON_ERROR);
    }
}
