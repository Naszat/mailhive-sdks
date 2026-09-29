<?php

declare(strict_types=1);

namespace Mailhive\Tests;

use Mailhive\Http\Response;
use Mailhive\Http\Transport;

final class FakeTransport implements Transport
{
    /** @var list<array{method: string, url: string, headers: array<string, string>, body: ?string}> */
    public array $requests = [];

    /** @param \Closure(array): Response $respond */
    public function __construct(private readonly \Closure $respond)
    {
    }

    public static function json(int $status, array $body, array $headers = []): self
    {
        return new self(static fn () => new Response($status, $headers, json_encode($body)));
    }

    public function send(string $method, string $url, array $headers, ?string $body, float $timeout): Response
    {
        $request = compact('method', 'url', 'headers', 'body');
        $this->requests[] = $request;
        return ($this->respond)($request);
    }

    public function lastBody(): array
    {
        return json_decode((string) end($this->requests)['body'], true);
    }
}
