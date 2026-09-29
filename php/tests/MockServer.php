<?php

declare(strict_types=1);

namespace Mailhive\Tests;

/** The shared mock API (mock-server/server.mjs), started once per run. */
final class MockServer
{
    private static ?string $url = null;
    /** @var resource|null */
    private static $process = null;

    public static function url(): string
    {
        if (self::$url === null) {
            $script = realpath(__DIR__ . '/../../mock-server/server.mjs');
            self::$process = proc_open(['node', $script, '--port', '0'], [1 => ['pipe', 'w']], $pipes);
            $line = (string) fgets($pipes[1]);
            self::$url = trim(substr($line, (int) strrpos($line, ' ')));
            register_shutdown_function(static fn () => proc_terminate(self::$process));
        }
        return self::$url;
    }

    public static function reset(): void
    {
        self::request('POST', '/__reset');
    }

    /** @return list<array{method: string, path: string, headers: array<string, string>, body: mixed}> */
    public static function requests(): array
    {
        return json_decode(self::request('GET', '/__requests'), true, 512, JSON_THROW_ON_ERROR);
    }

    private static function request(string $method, string $path): string
    {
        $context = stream_context_create(['http' => ['method' => $method, 'ignore_errors' => true]]);
        return (string) file_get_contents(self::url() . $path, false, $context);
    }
}
